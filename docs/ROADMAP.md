# Roadmap

Twenty-five tools are specified, labelled and open for contribution. Every one of them
has a tracking issue with acceptance criteria already written, so there is nothing to
design before you start — pick a row, comment on the issue, and it is yours.

This page is the canonical grouping. [`CHANGELOG.md`](../CHANGELOG.md) lists the same
set under `[Unreleased] → Planned`, and the [`good first issue`
list](https://github.com/juandresrodca/AdminForge/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
is the same set again, sorted by GitHub rather than by category.

**Every issue below carries `good first issue`.** The architecture is built so that a
new tool touches nothing outside its own folder, which is what makes that claim honest
rather than aspirational — see [`CONTRIBUTING.md`](../CONTRIBUTING.md) for the fifteen
minute walkthrough and [`ARCHITECTURE.md`](../ARCHITECTURE.md) for why it works.

So the useful signal here is not difficulty of the codebase, it is size of the problem
domain. That is what the **effort** column carries:

| Effort | What it means |
|---|---|
| **small** | Self-contained logic, no network call, a couple of hours. |
| **medium** | A data set to source, or one outbound request through the SSRF guard. |
| **large** | Real parsing — ASN.1, MIME headers, a specification to read first. |

---

## Start here

If this is your first contribution, these five are the shortest path from `git clone`
to a merged pull request. All are `small`, all are pure computation over the input, and
none of them touch the network — so there is no SSRF guard to reason about and no
fixture data to source.

| Issue | Tool |
|---|---|
| [#19](https://github.com/juandresrodca/AdminForge/issues/19) | Hash identifier |
| [#25](https://github.com/juandresrodca/AdminForge/issues/25) | PowerShell `-EncodedCommand` decoder |
| [#26](https://github.com/juandresrodca/AdminForge/issues/26) | Active Directory `userAccountControl` decoder |
| [#27](https://github.com/juandresrodca/AdminForge/issues/27) | Well-known SID resolver |
| [#34](https://github.com/juandresrodca/AdminForge/issues/34) | chmod and Unix permission calculator |

---

## Networking

Seven tools. The category the toolbox is most often reached for, and the one where the
SSRF guard does the most work — anything that resolves a hostname inherits it from the
core rather than implementing its own checks.

| Issue | Tool | What it does | Effort |
|---|---|---|---|
| [#6](https://github.com/juandresrodca/AdminForge/issues/6) | MAC address and OUI vendor lookup | Takes a MAC in any common notation and reports the organisation that registered the OUI. | medium |
| [#11](https://github.com/juandresrodca/AdminForge/issues/11) | IP geolocation and ASN lookup | Resolves an address to its autonomous system and the announcing organisation. | medium |
| [#12](https://github.com/juandresrodca/AdminForge/issues/12) | TCP port reachability checker | Opens a connection to a host and port and reports whether it answered, and how quickly. | medium |
| [#13](https://github.com/juandresrodca/AdminForge/issues/13) | Reverse DNS (PTR) bulk lookup | Resolves PTR records across a list of addresses or a small CIDR block. | small |
| [#14](https://github.com/juandresrodca/AdminForge/issues/14) | IP range to CIDR converter | Produces the smallest set of CIDR blocks covering a start and end address. | medium |
| [#15](https://github.com/juandresrodca/AdminForge/issues/15) | Wake-on-LAN magic packet builder | Builds the magic packet, shows the bytes, and optionally sends it. | small |
| [#16](https://github.com/juandresrodca/AdminForge/issues/16) | URL redirect chain tracer | Follows a URL through every hop and shows the status and target of each. | small |

## Certificates and crypto

Seven tools. Several of these handle material a user would not want leaving their
machine, so the browser-side versus server-side decision matters more here than
anywhere else in the toolbox — read the `In browser` / `Server side` labelling rules in
[`ARCHITECTURE.md`](../ARCHITECTURE.md) before choosing.

| Issue | Tool | What it does | Effort |
|---|---|---|---|
| [#17](https://github.com/juandresrodca/AdminForge/issues/17) | X.509 certificate decoder | Decodes a pasted PEM or base64 certificate without connecting to anything. | large |
| [#18](https://github.com/juandresrodca/AdminForge/issues/18) | CSR decoder | Shows what a certificate signing request actually asked for. | large |
| [#19](https://github.com/juandresrodca/AdminForge/issues/19) | Hash identifier | Takes a hash and reports which algorithms could have produced it. | small |
| [#20](https://github.com/juandresrodca/AdminForge/issues/20) | HMAC generator and verifier | Computes an HMAC over a message with a key, and verifies a supplied one. | medium |
| [#21](https://github.com/juandresrodca/AdminForge/issues/21) | bcrypt generator and verifier | Generates a bcrypt hash, and checks a password against an existing one. | large |
| [#22](https://github.com/juandresrodca/AdminForge/issues/22) | Pwned password checker | Checks a password against the HIBP corpus using k-anonymity, so the password never leaves the machine. | large |
| [#23](https://github.com/juandresrodca/AdminForge/issues/23) | security.txt validator | Fetches `/.well-known/security.txt` and checks it against RFC 9116. | small |

## Windows and Active Directory

Five tools. The category that distinguishes AdminForge from the developer-facing
toolboxes, and the one where the existing Windows error code lookup already sets the
pattern to follow.

| Issue | Tool | What it does | Effort |
|---|---|---|---|
| [#24](https://github.com/juandresrodca/AdminForge/issues/24) | Windows security event ID lookup | Explains an event ID, its interesting fields, and what it means in practice. | medium |
| [#25](https://github.com/juandresrodca/AdminForge/issues/25) | PowerShell `-EncodedCommand` decoder | Decodes the base64 payload back into readable script. | small |
| [#26](https://github.com/juandresrodca/AdminForge/issues/26) | `userAccountControl` decoder | Decodes the value into the flags it represents, and back again. | small |
| [#27](https://github.com/juandresrodca/AdminForge/issues/27) | Well-known SID resolver | Resolves a well-known SID and explains the structure of any SID. | small |
| [#28](https://github.com/juandresrodca/AdminForge/issues/28) | LDAP filter builder and validator | Validates a filter and explains in plain English what it matches. | large |

## Mail

Two tools, both extending the existing SPF, DKIM and DMARC checker rather than
duplicating it.

| Issue | Tool | What it does | Effort |
|---|---|---|---|
| [#29](https://github.com/juandresrodca/AdminForge/issues/29) | Email header analyser | Lays out the delivery path and the authentication results from a raw header block. | large |
| [#30](https://github.com/juandresrodca/AdminForge/issues/30) | DMARC record builder | Builds a valid record from choices, and explains what each one will do. | medium |

## Calculators

Four tools. Pure arithmetic over the input, no network, no data set to source — which
is exactly why they are the easiest place to learn the tool contract.

| Issue | Tool | What it does | Effort |
|---|---|---|---|
| [#31](https://github.com/juandresrodca/AdminForge/issues/31) | Uptime and SLA calculator | Converts between an availability percentage and the downtime it actually allows. | small |
| [#32](https://github.com/juandresrodca/AdminForge/issues/32) | RAID capacity calculator | Usable capacity, fault tolerance and rebuild exposure for an array. | medium |
| [#33](https://github.com/juandresrodca/AdminForge/issues/33) | Storage and data-rate converter | Converts storage and data-rate units, keeping decimal and binary prefixes straight. | small |
| [#34](https://github.com/juandresrodca/AdminForge/issues/34) | chmod calculator | Converts between symbolic and octal permissions, both directions, with an explanation. | small |

---

## Deliberately not listed

A handful of obvious utilities — GUID generator, timestamp converter, JSON formatter,
regex tester — are missing on purpose. Every browser toolbox already has them, they add
nothing to the case for self-hosting this one, and building them would spend
contributor time on the part of the problem that is already solved.

If you want one anyway, open a
[tool proposal](https://github.com/juandresrodca/AdminForge/issues/new?template=new_tool.yml)
and make the argument. The bar is that it has to be better here than in the tab the
reader already has open.

## Proposing something that is not on this list

The same route: a [tool proposal](https://github.com/juandresrodca/AdminForge/issues/new?template=new_tool.yml)
first, before you build. A proposal that gets accepted picks up the `new tool` label,
an effort estimate and a place in a category on this page.

What gets accepted is a tool that is **aimed at infrastructure work** and that is
**better for being self-hosted** — because the input is sensitive, because the target is
on your own network, or because the answer depends on something only your machine can
see. A tool that would be just as good as somebody else's hosted page is a tool this
project does not need to carry.

## When a tool ships

It moves out of this page and into [`CHANGELOG.md`](../CHANGELOG.md) under the release
that carried it, its row in the README's tool table is filled in, and the contributor
is credited by [all-contributors](https://allcontributors.org) — documentation, review
and bug reports counted alongside code.
