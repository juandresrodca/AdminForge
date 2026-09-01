# Security policy

AdminForge is a tool used by people doing security work, and a self-hosted instance
often sits inside a network worth protecting. Reports are taken seriously.

## Reporting a vulnerability

Please report privately through
[GitHub Security Advisories](https://github.com/juandresrodca/AdminForge/security/advisories/new)
rather than opening a public issue.

Include what you would want to receive: what the flaw is, how to reproduce it, and what
an attacker gets out of it. A working proof of concept is welcome but not required.

You can expect an acknowledgement within 72 hours and an assessment within a week. If a
fix is warranted I will agree a disclosure timeline with you, and credit you in the
advisory and the release notes unless you would rather I did not.

## What counts

AdminForge holds no accounts, no database and no user data, so the interesting attack
surface is narrower than most web applications. These are the areas that matter:

- **Server-side request forgery.** Any way to make a server-side tool contact a private,
  loopback, link-local or cloud-metadata address while `AllowPrivateTargets` is off.
  This is the highest-severity class of bug in the project.
- **Cross-site scripting.** Any way to get script to execute, including through a tool's
  rendered output. The content security policy blocks inline script, so a report should
  explain how the policy is bypassed.
- **Cross-site request forgery** against tool execution.
- **Denial of service** with modest effort — an input that makes a tool consume
  unbounded memory or CPU, or that gets past the response size and timeout caps.
- **Container escape or privilege escalation** from the published image.
- **Leaking tool input** into logs, error pages or outbound requests. Nothing a user
  types should be recorded anywhere.

## What does not

- **Reaching private addresses when `AdminForge:AllowPrivateTargets` is `true`.** That
  switch exists precisely to allow it, is off by default, and is documented as unsafe
  for an internet-facing instance.
- **A public demo being rate limited, or abused as a lookup service.** Run your own
  instance.
- **Missing security headers on a deployment you configured**, as opposed to on the
  application's own responses.
- **Vulnerabilities in a dependency with no exploitable path through AdminForge.**
  Report those upstream; Dependabot handles the routine bumps here.
- **Findings from an automated scanner** with no demonstrated impact.

## Supported versions

The `main` branch and the most recent published container image. AdminForge is
stateless and updates in place, so "pull the latest image" is always a valid fix.

## Running it safely

- Leave `AllowPrivateTargets` off unless the instance is unreachable from the internet.
- Put a reverse proxy with authentication in front of anything exposed publicly.
- Keep the rate limiter on.
- The image runs as an unprivileged user and needs no write access, so run it read-only:
  `docker run --read-only --tmpfs /tmp ...`

Client-side tools never send their input anywhere, which is why anything touching
secrets is built that way — but that guarantee is only as good as the instance serving
the page. Do not paste a production token into an AdminForge instance you do not
control.
