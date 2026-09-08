# Changelog

All notable changes to AdminForge are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Container images are published to
[`ghcr.io/juandresrodca/adminforge`](https://github.com/juandresrodca/AdminForge/pkgs/container/adminforge);
`latest` tracks `main`.

## [Unreleased]

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

[Unreleased]: https://github.com/juandresrodca/AdminForge/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/juandresrodca/AdminForge/releases/tag/v0.1.0

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
