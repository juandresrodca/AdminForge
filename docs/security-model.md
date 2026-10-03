# The outbound request security model

Six of AdminForge's thirteen tools take a hostname, an IP literal or a URL from
whoever is using the page and make a network request to it. That is server-side
request forgery as a feature: the whole value of the TLS checker is that it connects
to the host you typed. The question is therefore not whether AdminForge makes
attacker-chosen requests — it does, by design — but which targets it refuses, at
which moment, and what it cannot protect you from.

This page is the detail. [ARCHITECTURE.md — Security](../ARCHITECTURE.md#security)
has the shape of it in one diagram; read that first if you want the overview. What
follows is for two people: the operator deciding whether an instance can face the
internet, and the reviewer checking that the claims in the README are true.

Everything here describes the code in [`src/AdminForge.Core/Net/`](../src/AdminForge.Core/Net)
and the HTTP registration in
[`ServiceCollectionExtensions.cs`](../src/AdminForge.Core/ServiceCollectionExtensions.cs).
Where this page and the code disagree, the code is right and this page is a bug.

## The threat, stated precisely

An instance anyone can reach is an HTTP client anyone can aim. The attack is to aim
it inwards: at `127.0.0.1`, at `10.0.0.0/8`, at the hypervisor, at a Kubernetes API
server, and above all at `169.254.169.254`, where a cloud instance hands out
credentials to anything that asks from the right network. None of that needs a bug.
It needs only a tool that fetches a URL and an operator who never thought about it.

So the mitigation is not per tool. It lives in the core, every tool inherits it, and
a new tool gets it by taking `IOutboundTargetValidator` or `SafeHttpFetcher` as a
constructor dependency rather than an `HttpClient`.

## Three checkpoints, three different moments

| # | When | Where | What it does |
|---|---|---|---|
| 1 | Before any request | `OutboundTargetValidator.ValidateHostAsync` / `ValidateUrlAsync` | Resolves the host and refuses it if **any** resolved address is private or reserved. Returns the addresses it resolved. |
| 2 | Per redirect hop | `SafeHttpFetcher.FetchAsync` | Follows redirects by hand and puts every `Location` through checkpoint 1 again. |
| 3 | At the socket | `ConnectGuardedAsync`, the `ConnectCallback` on the shared `SocketsHttpHandler` | Resolves again immediately before connecting, filters the answer, and connects only to addresses that passed. |

Each one closes a window the one before it leaves open. Checkpoint 1 alone trusts
that DNS will answer the same way when the HTTP stack asks a second time, which is
precisely what a rebinding attack arranges not to happen. Checkpoint 2 exists because
a permitted public URL is free to answer `302 Location: http://169.254.169.254/`.
Checkpoint 3 exists because checkpoints 1 and 2 both end with a name, and names are
resolved by someone else later.

Redirects are followed manually for exactly this reason:
`AllowAutoRedirect = false` is set on the handler, so there is no code path in which
`HttpClient` follows a hop that nothing validated.

## What is refused

`IpAddressRules.IsPrivateOrReserved` is the single classifier. An IPv4-mapped IPv6
address such as `::ffff:127.0.0.1` is converted to its IPv4 value before any rule is
applied — otherwise it is a one-line bypass of the whole table. An address family
that is neither IPv4 nor IPv6 is refused rather than ignored.

### IPv4

| Range | Why it is refused |
|---|---|
| `0.0.0.0/8` | Unspecified; on Linux `0.0.0.0` reaches loopback |
| `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` | RFC 1918 private |
| `127.0.0.0/8` | Loopback — the instance's own services |
| `100.64.0.0/10` | Carrier-grade NAT |
| `169.254.0.0/16` | Link-local, **including cloud instance metadata at `169.254.169.254`** |
| `192.0.0.0/24` | IETF protocol assignments |
| `192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24` | TEST-NET-1, 2 and 3 |
| `192.88.99.0/24` | 6to4 relay anycast |
| `198.18.0.0/15` | Benchmarking |
| `224.0.0.0/4` and above | Multicast, reserved, broadcast |

### IPv6

| Range | Why it is refused |
|---|---|
| `::1`, `::` | Loopback and unspecified |
| `fe80::/10` | Link-local |
| `fec0::/10` | Site-local (deprecated, still routed by some stacks) |
| `fc00::/7` | Unique local |
| `ff00::/8` | Multicast |
| `2001:db8::/32` | Documentation |
| `2001:0::/32` | Teredo — tunnels to an embedded IPv4 |
| `::/96` | IPv4-compatible; carries an embedded address such as `::7f00:1` |
| `2002::/16` | 6to4; embeds an IPv4 that may well be private |
| `64:ff9b::/32` | NAT64 and local-use NAT64; resolves to an embedded IPv4 we cannot vet here |
| `100::/64` | Discard-only |

The IPv6 list is organised around one idea: refuse anything that is internal, and
refuse anything that *embeds or tunnels to* an address we would otherwise have to
vet. A range that does neither — ORCHID space, for instance — is not enumerated,
because reaching it is not a route into your network.

### Beyond the ranges

- **A mixed answer is refused outright.** If a name resolves to one public and one
  private address, the target is rejected rather than connected to the public one.
  Allowing it would make the behaviour depend on resolution order, which an attacker
  controls.
- **`localhost` is refused by name**, before any resolver is consulted, because what
  `localhost` resolves to varies by platform and container runtime. `*.localhost` too.
- **`BlockedHosts` is matched by name and by address.** A name entry also blocks its
  subdomains, so `example.com` covers `www.example.com`. Every resolved address is
  then checked against the same list, including CIDR entries — because blocking a
  name alone is bypassed by asking for its address, or for any other name pointing
  at it.
- **Only `http` and `https` reach a tool.** `file:`, `gopher:` and `ftp:` are
  rejected at parse time by `ValidateUrlAsync`.

## Which tool takes which path

| Tool | Runs | Outbound path |
|---|---|---|
| JWT decoder, password generator, hash generator, encoder/decoder | Browser | None — no request is made at all |
| Subnet calculator, cron expression parser, Windows error code lookup | Server | None — arithmetic, a time zone database, a dataset in the image |
| RDAP and WHOIS lookup, HTTP security headers, security.txt validator | Server | `SafeHttpFetcher` — all three checkpoints |
| TLS certificate checker | Server | `IOutboundTargetValidator`, then connects to **the addresses validation returned**, not to the name again |
| DNS lookup | Server | `IOutboundTargetValidator` on the resolver, then `DnsClient` |
| SPF, DKIM and DMARC checker | Server | `IOutboundTargetValidator` on the domain, then `DnsClient` |

The TLS checker is worth a sentence of its own. It does not re-resolve: it hands
`validation.Addresses` straight to `Socket.ConnectAsync`, so there is no second
lookup to poison. It also completes the handshake even when certificate validation
fails, deliberately — inspecting a broken certificate is why anyone opens that tool.

The two DNS tools are a different shape. They do not connect to the name you type;
they ask a resolver about it, so the name itself needs no address rules. What does
need them is a **custom resolver**, which is a user-supplied network target like any
other and goes through checkpoint 1 before a query is sent to it.

## `AllowPrivateTargets`: the one setting that changes the answer

Default `false`. Set it to `true` and both checkpoint 1 and checkpoint 3 stop
refusing private and reserved addresses — the classifier is still there, its verdict
is simply no longer acted on.

| | `false` (default) | `true` |
|---|---|---|
| Server-side tools may reach | Public addresses only | Also loopback, RFC 1918, link-local, CGNAT, ULA |
| TLS checker against `192.168.1.10` | Refused | Works |
| `localhost` by name | Refused | Allowed |
| Instance reachable from the internet | Safe | **An open SSRF proxy into its network, metadata endpoints included** |

That last cell is not a caution, it is a description. An internet-facing instance
with the switch on will fetch `http://169.254.169.254/latest/meta-data/iam/security-credentials/`
for anybody who asks, and render the answer on the page.

Turn it on for an instance on a network that cannot be reached from outside, where
pointing the certificate checker at an internal host is the entire reason you are
self-hosting. Leave it off everywhere else.

### What it does not switch off

`BlockedHosts` is enforced regardless of `AllowPrivateTargets`, at both checkpoint 1
and checkpoint 3. That makes a useful middle setting possible: open the private
ranges you need and keep the ones you do not out of reach.

```yaml
environment:
  AdminForge__AllowPrivateTargets: "true"        # homelab: internal targets are the point
  AdminForge__BlockedHosts__0: "169.254.169.254" # but never the metadata endpoint
  AdminForge__BlockedHosts__1: "10.10.0.0/24"    # nor the hypervisor management network
```

An entry that is neither a host, an address nor a CIDR range is ignored rather than
fatal, so check a typo here by testing a refusal rather than by waiting for a
startup error.

## Caps, and what they are for

| Setting | Default | Guards against |
|---|---|---|
| `MaxResponseBytes` | 2 MiB | A target that streams forever. The body is read to the cap and marked truncated. |
| `ToolTimeoutSeconds` | 15 | A target that accepts the connection and then says nothing. |
| `MaxRedirects` | 5 | A redirect loop. Each hop is validated, so a loop costs validations as well as requests. |
| `RateLimit__PermitLimit` / `WindowSeconds` | 30 per 60s | Using the instance as a scanner or an amplifier. Partitioned per client — see the forwarded-header note in [self-hosting](self-hosting.md#behind-a-reverse-proxy--and-why-it-is-not-optional). |

Invalid values for the first three fail at startup rather than at the first request.

## Non-goals

- **This is not an egress firewall.** It governs the targets AdminForge chooses to
  contact. It has no opinion about anything else on the host, and it is not a reason
  to skip network policy on a container that can reach a network worth reaching.
- **It makes no claim about public targets.** Any public address is fair game, which
  includes a public address belonging to you. If your perimeter publishes something
  that trusts its source address, AdminForge can be aimed at it.
- **It does not vet content.** A response that passes every checkpoint is still
  attacker-controlled text; it is escaped before rendering, not trusted.
- **AdminForge makes no requests of its own.** No CDN, no analytics, no telemetry, no
  web fonts. Every outbound request corresponds to a target someone typed, which is
  what makes an air-gapped instance coherent.

## Known limitations

Stated here because an overstated guarantee is worse than none.

- **The TLS checker accepts any port from 1 to 65535.** Its failure message is the
  same either way, but the *time it takes* is not: a closed port refuses immediately
  and a filtered one waits out the connect timeout. Against public addresses that is a
  port-state oracle with a 30-per-minute budget. The rate limiter is the only thing
  bounding it; a public instance that cares should sit behind authentication.
- **A custom DNS resolver is used by its first resolved address only.** If a name
  given as a resolver resolves to several addresses, checkpoint 1 vets them all and
  then `validation.Addresses[0]` is the one queried.
- **`MaxResponseBytes` is allocated eagerly**, one buffer per in-flight fetch, so the
  memory ceiling is the cap times concurrency rather than the cap. Lower it on a small
  host, or leave the rate limiter on, or both.
- **Checkpoint 3 resolves the name itself** rather than reusing checkpoint 1's
  answer. It filters that answer and connects only to addresses that passed, so there
  is no window between the check and the connection — but a target whose DNS flaps
  between two *public* addresses can still be connected to either, and the hop
  recorded in the response is the name, not the address.
- **A CNAME chain is judged by its final addresses**, which is correct, but means a
  `BlockedHosts` name entry does not block an alias that reaches the same host under
  a different name with a different address.

Anything in this list that turns out to be exploitable rather than merely true is a
vulnerability, not a documented limitation. Report it — see
[SECURITY.md](../SECURITY.md).

## Related

- [ARCHITECTURE.md — Security](../ARCHITECTURE.md#security) — the three checkpoints as
  one diagram, in the context of the request flow
- [docs/self-hosting.md](self-hosting.md) — the operational side: reverse proxy,
  trusted proxies, air-gapped deployment
- [SECURITY.md](../SECURITY.md) — what counts as a vulnerability here, and how to
  report one
- [README — Configuration](../README.md#configuration) — every setting and its default
