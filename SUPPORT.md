# Getting help

AdminForge is one container with no accounts and no telemetry, so a question about it is
almost always one of three things. Pick the matching route and the answer arrives faster
than a general issue would.

## "I cannot get it running"

Start with the [one-container quickstart](docs/self-hosting.md#one-container), and run it
that way **before** compose, a reverse proxy or TLS. A default container on
<http://localhost:8080> either works or fails somewhere obvious, which is most of the
diagnosis. [docs/self-hosting.md](docs/self-hosting.md) then covers the rest: the reverse
proxy and why it is not optional, the authentication AdminForge deliberately does not
have, `AllowPrivateTargets` for a homelab, air-gapped hosts, updating and the health
endpoint.

If that guide does not cover your setup, or is wrong about it, ask in
[Discussions](https://github.com/juandresrodca/AdminForge/discussions) with the command
you ran and what the container logged (`docker logs adminforge`).

## "A tool is giving me the wrong answer"

That is the most serious kind of bug this project can have — people make changes based on
what AdminForge tells them. Open a
[bug report](https://github.com/juandresrodca/AdminForge/issues/new?template=bug_report.yml)
and include the input you used, because that is what becomes the regression test.

If the question is really *"is it safe to paste this in here"*, the answer is in
[docs/tools/README.md](docs/tools/README.md) and you do not have to ask: it lists every
tool with whether it makes an outbound request at all. Four of the thirteen never send
your input anywhere — the JWT decoder, password generator, hash generator, and encoder and
decoder all run in your browser. Three more reach your instance but never the network. The
remaining six aim a request at a host you supply, which is what
[docs/security-model.md](docs/security-model.md) is about.

## "This should not be public"

A vulnerability — anything that lets a server-side tool reach an address it should not, or
gets script to execute — goes through
[Security → Report a vulnerability](https://github.com/juandresrodca/AdminForge/security/advisories/new),
never a public issue. [SECURITY.md](SECURITY.md) says what counts and what to expect.

---

## Or skip the question and build the thing

The open issues labelled
[`good first issue`](https://github.com/juandresrodca/AdminForge/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
are unassigned and each already carries acceptance criteria, so commenting on one is
enough to claim it. [CONTRIBUTING.md](CONTRIBUTING.md) is the walkthrough from
`git clone` to a pull request — a new tool is three files in one new folder and nothing
modified.

**On timing:** this is a side project with one maintainer, so an issue or a discussion
thread may sit for a while and this file is not going to pretend otherwise. Security
reports are the exception, and [SECURITY.md](SECURITY.md) states the window those are held
to.
