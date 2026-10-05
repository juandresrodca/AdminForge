# Changelog

All notable changes to AdminForge are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Container images are published to
[`ghcr.io/juandresrodca/adminforge`](https://github.com/juandresrodca/AdminForge/pkgs/container/adminforge);
`latest` tracks `main`.

`0.1.0` predates the release pipeline. It is described below and it is a real cut of the
code — commit [`047a2de`](https://github.com/juandresrodca/AdminForge/commit/047a2de),
the last commit of 1 September 2026 — but it carries no git tag and no GitHub release,
so there is no `v0.1.0` to compare against and no `0.1.0` image on GHCR either: the
semver image tags are cut from a `v*` tag push, and `main` and `latest` are the only tags
that exist today. The links at the foot of this file therefore point at commits rather
than tags. Tagging starts with the next version; the first tag push also produces the
first version-pinned image.

## [Unreleased]

### Added

- [`ARCHITECTURE.md`](ARCHITECTURE.md): three Mermaid diagrams for the parts that were
  prose only — the startup discovery pipeline and its fail-the-build gate, the request
  path through the single controller including the progressive-enhancement branch, and
  the three outbound-target checkpoints and the window each one closes. GitHub renders
  them inline; the existing ASCII diagrams are untouched.
- [`docs/self-hosting.md`](docs/self-hosting.md): the deployment half of the README —
  reverse proxy and TLS, why a proxy is not optional given that `UseForwardedHeaders`
  trusts `X-Forwarded-For` from any caller and the rate limiter partitions on the
  result, the `AllowPrivateTargets` decision table, the seven tools that work with no
  egress and how to load the image onto an air-gapped host, and updating.

### Changed

- **Rate limiting can no longer be bypassed with a forged `X-Forwarded-For`.** The
  header is now believed only from loopback and from the new `AdminForge:TrustedProxies`
  setting (IPs or CIDR ranges). A proxy that is not on loopback — e.g. a proxy container
  on the same Docker network — must be listed there, or every user shares one bucket.
  An entry that is neither an address nor a CIDR range stops the app at startup, because
  silently trusting nobody throttles every user of the instance as one client. ([#39])
- `BlockedHosts` also blocks subdomains of a blocked name, and entries may be IP
  addresses or CIDR ranges checked against every resolved address, including at connect time.
- IPv4-compatible (`::/96`), 6to4 (`2002::/16`) and discard-only (`100::/64`) IPv6
  addresses are refused as targets.

### Fixed

- SPF, DKIM and DMARC checker: SPF is read term by term, so a bare `all` is reported as
  pass-anything, `-all` inside another term is no longer mistaken for the all mechanism,
  `a/24` and `mx/24` count as lookups, and the lookup count follows nested includes and
  redirects. Duplicate SPF or DMARC records are flagged as the errors they are. A DKIM
  key is recognised by its `p=` tag, and an empty one is shown as revoked.
- TLS certificate checker: the chain table is populated. The chain was read after
  `SslStream` had already disposed it.
- An unknown tool returns a real 404 instead of a redirect; `/error/{code}` no longer
  throws on a code outside 400–599; and a throttled POST is reported as 429 rather than 405.
- Subnet calculator rejects IPv4 shorthand such as `10/8`, which parsed as `0.0.0.10/8`.
- security.txt validator requires `Expires` in RFC 3339 format, as RFC 9116 does.
- Cron parser accepts tabs between fields.

### Planned

Tools accepted into the roadmap and open for contribution. Grouped the same way as
[`docs/ROADMAP.md`](docs/ROADMAP.md); each links to its tracking issue.

- **Networking** — MAC/OUI vendor lookup ([#6]), IP geolocation and ASN lookup ([#11]),
  TCP port reachability checker ([#12]), bulk reverse DNS ([#13]), IP range to CIDR ([#14]),
  Wake-on-LAN packet builder ([#15]), URL redirect chain tracer ([#16]).
- **Certificates and crypto** — X.509 certificate decoder ([#17]), CSR decoder ([#18]),
  hash identifier ([#19]), HMAC generator and verifier ([#20]), bcrypt generator and
  verifier ([#21]), pwned-password check over HIBP k-anonymity ([#22]),
  security.txt validator ([#23]).
- **Windows and Active Directory** — security event ID lookup ([#24]), PowerShell
  `-EncodedCommand` decoder ([#25]), `userAccountControl` decoder ([#26]),
  well-known SID resolver ([#27]), LDAP filter builder and validator ([#28]).
- **Mail** — email header analyser ([#29]), DMARC record builder ([#30]).
- **Calculators** — uptime and SLA percentage ([#31]), RAID capacity ([#32]),
  storage and data-rate units ([#33]), chmod and Unix permissions ([#34]).

New tools are added through the scaffold described in
[`CONTRIBUTING.md`](CONTRIBUTING.md); a tool is one folder under
`src/AdminForge.Tools/<Category>/<ToolName>` plus a test.

## [0.1.0] - 2026-09-01

First public cut. A self-hosted, single-container toolbox with twelve working tools,
no database, no accounts and no configuration file.

### Added

- **Tool platform** — the tool contract, the registry that discovers tools at startup,
  and the shared result model that gives every tool the same output shape.
- **Twelve seed tools**, across five categories:
  - *Network* — subnet calculator, DNS lookup, RDAP lookup.
  - *Security* — TLS certificate inspector, JWT decoder, hash generator,
    password generator, security-header analyser.
  - *Email* — mail authentication (SPF/DKIM/DMARC) lookup.
  - *Windows* — Windows error-code decoder.
  - *Encoding* — text encoder/decoder.
  - *Ops* — cron expression parser.
- **Hardened outbound HTTP stack with an SSRF guard.** Tools that resolve a hostname
  refuse private, loopback, link-local and metadata-service ranges unless
  `AdminForge__AllowPrivateTargets` is explicitly enabled for homelab use.
- **Web shell** — ASP.NET Core MVC front end with the scout theme and the tool gallery.
- **Container packaging** — `Dockerfile` and `docker-compose.yml`, published to GHCR,
  runnable read-only with a `tmpfs` for `/tmp`.
- **Tool scaffold** (`tools/`) for generating a new tool folder with its test.
- **Tests** covering the tool contract, the address arithmetic and the SSRF rules.
- **Project governance** — MIT licence, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `ARCHITECTURE.md`, issue templates and all-contributors recognition.
- **CI** — build and test on push and pull request; a separate workflow publishes the
  container image.

### Security

- No inbound authentication surface: AdminForge stores nothing and has no accounts,
  so there is no session, cookie or credential to steal.
- Outbound requests are constrained by the SSRF guard described above. Exposing an
  instance to the internet with `AllowPrivateTargets` enabled turns it into an internal
  network scanner — the default is off, and `docker-compose.yml` documents why.

[Unreleased]: https://github.com/juandresrodca/AdminForge/compare/047a2de...HEAD
[0.1.0]: https://github.com/juandresrodca/AdminForge/commit/047a2de

[#6]: https://github.com/juandresrodca/AdminForge/issues/6
[#11]: https://github.com/juandresrodca/AdminForge/issues/11
[#12]: https://github.com/juandresrodca/AdminForge/issues/12
[#13]: https://github.com/juandresrodca/AdminForge/issues/13
[#14]: https://github.com/juandresrodca/AdminForge/issues/14
[#15]: https://github.com/juandresrodca/AdminForge/issues/15
[#16]: https://github.com/juandresrodca/AdminForge/issues/16
[#17]: https://github.com/juandresrodca/AdminForge/issues/17
[#18]: https://github.com/juandresrodca/AdminForge/issues/18
[#19]: https://github.com/juandresrodca/AdminForge/issues/19
[#20]: https://github.com/juandresrodca/AdminForge/issues/20
[#21]: https://github.com/juandresrodca/AdminForge/issues/21
[#22]: https://github.com/juandresrodca/AdminForge/issues/22
[#23]: https://github.com/juandresrodca/AdminForge/issues/23
[#24]: https://github.com/juandresrodca/AdminForge/issues/24
[#25]: https://github.com/juandresrodca/AdminForge/issues/25
[#26]: https://github.com/juandresrodca/AdminForge/issues/26
[#27]: https://github.com/juandresrodca/AdminForge/issues/27
[#28]: https://github.com/juandresrodca/AdminForge/issues/28
[#29]: https://github.com/juandresrodca/AdminForge/issues/29
[#30]: https://github.com/juandresrodca/AdminForge/issues/30
[#31]: https://github.com/juandresrodca/AdminForge/issues/31
[#32]: https://github.com/juandresrodca/AdminForge/issues/32
[#33]: https://github.com/juandresrodca/AdminForge/issues/33
[#34]: https://github.com/juandresrodca/AdminForge/issues/34
[#39]: https://github.com/juandresrodca/AdminForge/issues/39
