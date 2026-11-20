#!/usr/bin/env python3
"""
Regenerates the embedded IEEE OUI registry used by the MAC address lookup.

The output is src/AdminForge.Tools/Network/MacAddressLookup/oui-registry.json.gz:
every MA-L (24-bit), MA-M (28-bit) and MA-S (36-bit) assignment, mapped to the
organisation name the IEEE publishes for it. Running the script twice on the same
data produces byte-identical output, so a refresh pull request only shows a diff
when the registry actually changed.

Usage, from the repository root:

  # Straight from the IEEE (the default):
  python3 tools/refresh-oui-registry.py

  # From CSVs you downloaded yourself, e.g. on a machine without that access.
  # The folder must hold oui.csv, mam.csv and oui36.csv; iab.csv is optional.
  python3 tools/refresh-oui-registry.py --ieee-dir ~/Downloads/ieee

  # From the index.json of the oui-data npm package (BSD-2-Clause), which is the
  # same IEEE registry already converted to JSON:
  npm pack oui-data && tar -xzf oui-data-*.tgz
  python3 tools/refresh-oui-registry.py --oui-data package/index.json

The CSVs are published at:
  https://standards-oui.ieee.org/oui/oui.csv        MA-L
  https://standards-oui.ieee.org/oui28/mam.csv      MA-M
  https://standards-oui.ieee.org/oui36/oui36.csv    MA-S
  https://standards-oui.ieee.org/iab/iab.csv        IAB, the 36-bit blocks that preceded MA-S

Standard library only, so it runs anywhere Python 3.9 or later does.
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import gzip
import io
import json
import pathlib
import re
import sys
import urllib.request

IEEE_BASE = "https://standards-oui.ieee.org"

# File name, URL path, registry it is filed under, and whether it must exist.
IEEE_FILES = [
    ("oui.csv", "/oui/oui.csv", "MA-L", True),
    ("mam.csv", "/oui28/mam.csv", "MA-M", True),
    ("oui36.csv", "/oui36/oui36.csv", "MA-S", True),
    # IAB blocks are 36 bits wide like MA-S, and are looked up the same way.
    ("iab.csv", "/iab/iab.csv", "MA-S", False),
]

# Hex digits per registry: 24, 28 and 36 bits.
WIDTH = {"MA-L": 6, "MA-M": 7, "MA-S": 9}
REGISTRY_FOR_WIDTH = {width: name for name, width in WIDTH.items()}

DEFAULT_OUTPUT = (
    pathlib.Path(__file__).resolve().parent.parent
    / "src/AdminForge.Tools/Network/MacAddressLookup/oui-registry.json.gz"
)

HEX = re.compile(r"^[0-9A-F]+$")


def clean_name(value: str) -> str:
    """One line, single spaces, nothing around it."""
    return " ".join(value.split())


def add(registry: dict[str, dict[str, str]], name: str, assignment: str, organisation: str) -> None:
    prefix = assignment.strip().upper().replace("-", "").replace(":", "")
    organisation = clean_name(organisation)

    if len(prefix) != WIDTH[name] or not HEX.match(prefix) or not organisation:
        raise ValueError(f"unexpected {name} row: {assignment!r} {organisation!r}")

    registry[name][prefix] = organisation


def read_csv(text: str, name: str, registry: dict[str, dict[str, str]]) -> int:
    rows = 0

    for row in csv.DictReader(io.StringIO(text)):
        add(registry, name, row["Assignment"], row["Organization Name"])
        rows += 1

    return rows


def fetch(url: str) -> str:
    request = urllib.request.Request(url, headers={"User-Agent": "AdminForge OUI refresh"})

    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read().decode("utf-8-sig")


def load_ieee(directory: pathlib.Path | None) -> tuple[dict[str, dict[str, str]], str]:
    registry: dict[str, dict[str, str]] = {name: {} for name in WIDTH}

    for file_name, path, name, required in IEEE_FILES:
        try:
            if directory is None:
                text = fetch(IEEE_BASE + path)
            else:
                text = (directory / file_name).read_text(encoding="utf-8-sig")
        except (OSError, ValueError) as error:
            if required:
                raise SystemExit(f"Could not read {file_name}: {error}") from error
            print(f"  skipped {file_name}: {error}", file=sys.stderr)
            continue

        print(f"  {file_name}: {read_csv(text, name, registry)} rows", file=sys.stderr)

    source = "IEEE Registration Authority CSV files" + ("" if directory is None else " (local copy)")
    return registry, source


def load_oui_data(path: pathlib.Path) -> tuple[dict[str, dict[str, str]], str]:
    registry: dict[str, dict[str, str]] = {name: {} for name in WIDTH}
    data = json.loads(path.read_text(encoding="utf-8"))

    for prefix, value in data.items():
        name = REGISTRY_FOR_WIDTH.get(len(prefix))

        if name is None:
            raise SystemExit(f"Unexpected prefix length in {path}: {prefix!r}")

        # The package stores the organisation and its address on separate lines.
        add(registry, name, prefix, value.split("\n", 1)[0])

    return registry, "IEEE Registration Authority, via the oui-data npm package"


def write(registry: dict[str, dict[str, str]], source: str, output: pathlib.Path) -> None:
    document = {
        "source": source,
        "generated": dt.datetime.now(dt.timezone.utc).date().isoformat(),
        **{name: dict(sorted(entries.items())) for name, entries in registry.items()},
    }

    payload = json.dumps(document, ensure_ascii=False, separators=(",", ":")).encode("utf-8")

    buffer = io.BytesIO()

    # mtime=0 and no file name keep the gzip header identical between runs.
    with gzip.GzipFile(filename="", fileobj=buffer, mode="wb", compresslevel=9, mtime=0) as archive:
        archive.write(payload)

    output.write_bytes(buffer.getvalue())

    counts = ", ".join(f"{name} {len(entries):,}" for name, entries in registry.items())
    print(f"Wrote {output} ({len(buffer.getvalue()):,} bytes): {counts}", file=sys.stderr)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--ieee-dir", type=pathlib.Path, help="read the IEEE CSVs from this folder")
    source.add_argument("--oui-data", type=pathlib.Path, help="read index.json from the oui-data npm package")
    parser.add_argument("--output", type=pathlib.Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    if args.oui_data is not None:
        registry, description = load_oui_data(args.oui_data)
    else:
        registry, description = load_ieee(args.ieee_dir)

    # A registry this small means a download went wrong, not that the IEEE shrank.
    if len(registry["MA-L"]) < 30000:
        raise SystemExit(f"Only {len(registry['MA-L'])} MA-L entries were read; refusing to write.")

    write(registry, description, args.output)


if __name__ == "__main__":
    main()
