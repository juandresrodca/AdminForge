# Self-hosting AdminForge

The README gets you a running instance in one `docker run`. This page is the rest of
it: putting it behind a reverse proxy, deciding what the network tools are allowed to
reach, running it without internet access, and keeping it up to date.

Nothing here is required to *try* AdminForge. All of it matters once other people can
reach the instance.

## What it needs

| | |
|---|---|
| State | None. No database, no volumes, no config file required. The container is happy `--read-only` with a tmpfs on `/tmp`. |
| Ports | One: `8080` inside the container. |
| Architectures | `linux/amd64` and `linux/arm64` — a Raspberry Pi or an Apple silicon Mac pulls the same tag. |
| Outbound network | Only for the server-side tools, and only to the target you type. See [Running without internet access](#running-without-internet-access). |
| Privileges | None. The image runs as an unprivileged user and wants `no-new-privileges`. |

## One container

```bash
docker run -d --name adminforge \
  -p 8080:8080 \
  --restart unless-stopped \
  --read-only --tmpfs /tmp \
  --security-opt no-new-privileges:true \
  -e AdminForge__AllowPrivateTargets=false \
  ghcr.io/juandresrodca/adminforge:latest
```

`--read-only` is not decoration: AdminForge never writes to its own filesystem, so if
the container ever needs to, something is wrong and you want it to fail rather than
succeed.

## Compose

[`docker-compose.yml`](../docker-compose.yml) in the repository root is a complete,
hardened service definition — read-only root, no-new-privileges, healthcheck, the
settings that matter set explicitly. It **builds from source** by default, which is
what a contributor wants and not what a self-hoster wants. Swap the two build lines
for the published image:

```yaml
services:
  adminforge:
    image: ghcr.io/juandresrodca/adminforge:latest   # replaces `build: .`
```

Then `docker compose up -d`, and `docker compose logs -f adminforge` if it does not
come up.

## Behind a reverse proxy — and why it is not optional

Put AdminForge behind a proxy that terminates TLS. Beyond the usual reasons, there is
one specific to this application.

`Program.cs` enables `UseForwardedHeaders` with `KnownProxies` and `KnownIPNetworks`
both cleared, which is the standard configuration for a container whose proxy address
is not known ahead of time. The consequence is that `X-Forwarded-For` is trusted from
*any* caller, and the rate limiter partitions on the resulting client address. Behind
a proxy that sets the header itself, that is exactly right. Exposed directly to a
network, a caller can send a different `X-Forwarded-For` on every request and never
meet the rate limit at all.

So: terminate at a proxy, make sure the proxy **sets** `X-Forwarded-For` rather than
appending to whatever the client sent, and do not publish container port 8080 to
anything but the proxy.

### nginx

```nginx
server {
    listen 443 ssl http2;
    server_name tools.example.com;

    ssl_certificate     /etc/letsencrypt/live/tools.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/tools.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $remote_addr;   # set, not append
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### Caddy

```caddyfile
tools.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

Caddy gets the certificate itself and sets the forwarded headers correctly by default.

AdminForge emits HSTS outside Development and sets its own security headers and
content security policy, so the proxy does not need to add any — and should not
rewrite the CSP, which is strict on purpose.

## There is no login

AdminForge has no accounts, by design: there is nothing to log in to, no per-user
state and nothing stored. That also means **anything that can reach the instance can
use it**. For anything beyond a LAN, put authentication in front of it — proxy basic
auth for one person, an identity-aware proxy for a team, or simply keep it on a
private network or an overlay such as Tailscale or WireGuard and skip the internet
entirely.

An unauthenticated public instance is a reasonable thing to run on purpose — set
`AdminForge__InstanceBanner` to say so — but do it knowing that its server-side tools
make outbound requests on your address, within the target rules below.

## Homelab or internet-facing: the one setting that matters

`AllowPrivateTargets` is the decision. Everything else in
[Configuration](../README.md#configuration) is a tuning knob.

| | `AllowPrivateTargets=false` (default) | `AllowPrivateTargets=true` |
|---|---|---|
| Server-side tools may reach | Public addresses only | Also private, loopback, link-local, CGNAT and unique-local |
| Point the TLS checker at `192.168.1.10` | Refused | Works |
| If the instance is internet-reachable | Safe | **An open SSRF proxy into your network, including cloud metadata endpoints** |

Turn it on for an instance on your own network that cannot be reached from outside,
because checking a certificate on an internal host is the whole point of running your
own. Leave it off everywhere else. The refusals cover DNS rebinding too: a name that
resolves to a mix of public and private addresses is rejected outright, and every
redirect hop is re-validated — see [ARCHITECTURE.md](../ARCHITECTURE.md#security).

## Running without internet access

Seven of the twelve tools need no egress at all, which makes an air-gapped instance
genuinely useful rather than a stub.

| Works offline | Needs outbound access |
|---|---|
| JWT decoder, password generator, hash generator, encoder/decoder — these run in the browser and post nothing | DNS lookup |
| Subnet calculator — pure arithmetic on the server | RDAP and WHOIS lookup |
| Windows error code lookup — the dataset ships in the image | TLS certificate checker |
| Cron expression parser — no network, just a time zone database | HTTP security headers |
| | SPF, DKIM and DMARC checker |

Getting the image onto a disconnected host:

```bash
# on a machine with a registry route
docker pull ghcr.io/juandresrodca/adminforge:latest
docker save ghcr.io/juandresrodca/adminforge:latest | gzip > adminforge.tar.gz

# on the air-gapped host
gunzip -c adminforge.tar.gz | docker load
```

The four browser-side tools are the reason a locked-down instance is worth the
trouble: a JWT or a password never leaves the machine it was typed on — no request is
made at all — so pasting a production token into your own AdminForge is materially
different from pasting it into a website. Every tool page carries a badge saying which
side it runs on; trust the badge rather than this table if the two ever disagree.

## Updating

```bash
docker pull ghcr.io/juandresrodca/adminforge:latest
docker stop adminforge && docker rm adminforge      # then re-run the run command
# or, with compose:
docker compose pull && docker compose up -d
```

There is no data to migrate and no downtime to plan for beyond the seconds the
container takes to restart. For a reproducible deployment, pin the digest
(`ghcr.io/juandresrodca/adminforge@sha256:…`) and bump it deliberately; `latest`
tracks `main`, and `main` moves.

## Health and monitoring

`GET /healthz` returns `{"status":"ok"}` with no authentication, which is what the
container's own `HEALTHCHECK` and any uptime monitor should poll. It is a liveness
probe: it says the process is answering, not that an outbound lookup would succeed.

A tool run rejected by the rate limiter answers `429`, so a sudden run of 429s in the
proxy log is either a genuinely busy instance or the forwarded-header problem above.

## Related

- [README — Configuration](../README.md#configuration) — every setting and its default
- [ARCHITECTURE.md — Security](../ARCHITECTURE.md#security) — how the target rules and
  the CSP are implemented, in one place, for every tool
- [SECURITY.md](../SECURITY.md) — reporting a vulnerability
