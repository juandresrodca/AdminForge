# Tool reference

Every tool AdminForge ships, what question it answers, what it accepts, and — the part
that decides whether you can use it on real data — **where it runs**.

The UI labels each tool `In browser` or `Server side`. That label is honest but coarse,
because "server side" covers two very different things: arithmetic the instance does
locally, and a request the instance makes to a host you named. Only the second kind can
leak anything, and only the second kind passes through the
[outbound request security model](../security-model.md). This page splits them.

Each tool lives at `/tools/<id>` on your instance — so the subnet calculator on a local
container is <http://localhost:8080/tools/subnet-calculator>.

---

## Where each tool runs

| Tool | Id | Runs | Makes an outbound request? |
|---|---|---|---|
| [JWT decoder](#jwt-decoder) | `jwt-decoder` | Browser | No — nothing is posted at all |
| [Password generator](#password-generator) | `password-generator` | Browser | No |
| [Hash generator](#hash-generator) | `hash-generator` | Browser | No |
| [Encoder and decoder](#encoder-and-decoder) | `text-encoder` | Browser | No |
| [Subnet calculator](#subnet-calculator) | `subnet-calculator` | Server | No — pure arithmetic |
| [Cron expression parser](#cron-expression-parser) | `cron-parser` | Server | No — pure arithmetic |
| [Windows error code lookup](#windows-error-code-lookup) | `windows-error-code` | Server | No — embedded dataset |
| [DNS lookup](#dns-lookup) | `dns-lookup` | Server | **Yes** — DNS, to your resolver or one you pick |
| [TLS certificate checker](#tls-certificate-checker) | `tls-certificate-checker` | Server | **Yes** — a TLS handshake to the host and port |
| [HTTP security headers](#http-security-headers) | `http-security-headers` | Server | **Yes** — an HTTP request to the URL |
| [security.txt validator](#securitytxt-validator) | `security-txt-validator` | Server | **Yes** — HTTPS to two well-known paths |
| [SPF, DKIM and DMARC checker](#spf-dkim-and-dmarc-checker) | `mail-auth-checker` | Server | **Yes** — DNS only, no mail is sent |
| [RDAP and WHOIS lookup](#rdap-and-whois-lookup) | `rdap-lookup` | Server | **Yes** — HTTPS to `rdap.org` and the registry it redirects to |

Read that last column as the blast radius. Four tools never post your input anywhere,
so a production token or a password is safe in them. Three more reach the server but
never the network, so the input stays on your instance. The remaining six aim a request
at a target you supply, which is server-side request forgery as a feature: they are the
reason the `AllowPrivateTargets` setting exists and the reason
[docs/security-model.md](../security-model.md) is as long as it is.

**Rate limiting applies to the nine server-side tools only** — the browser tools never
reach the server, so there is nothing to limit. The default is 30 runs per client per
60 seconds (`RateLimit__PermitLimit`, `RateLimit__WindowSeconds`), and a single run is
capped at 15 seconds (`ToolTimeoutSeconds`) and 2 MiB of fetched response
(`MaxResponseBytes`).

---

## Network

### Subnet calculator

**Answers:** what are the network, broadcast, usable range, mask, wildcard and host
count for this block — and what does it look like split into equal subnets.

| Field | Accepts |
|---|---|
| Network (required) | IPv4 or IPv6, up to 128 characters. `10.20.0.0/22`, `10.20.0.5/22` or `10.20.0.0 255.255.252.0` all work. |
| Split into / | Optional prefix length, 1–128. Divides the block into equal subnets. |
| Show first | 1–256, default 16. How many of those subnets to list. |

**Limits.** Pure arithmetic — nothing you type leaves the instance and no network call
is made. A `/31` is treated as an RFC 3021 point-to-point link and a `/32` as a single
host, so neither reports a broadcast address. The class letter in the output is
historical trivia; classful addressing has been obsolete since CIDR.

### DNS lookup

**Answers:** what does DNS actually return for this name, from this resolver, right now.

| Field | Accepts |
|---|---|
| Domain or host (required) | Up to 253 characters. |
| Record type | A, AAAA, MX, TXT, NS, CNAME, SOA, CAA, SRV or PTR. Default A. |
| Resolver | Optional IP, up to 45 characters. Defaults to the instance's system resolver. |

**Limits.** The query leaves the AdminForge server, so the answer is what *that machine*
sees — which is the point when you are checking a split-horizon zone or a resolver that
filters, and a trap if you assumed it was your laptop's view. Caching is disabled, so
every run is a fresh query. A resolver on a private address is refused unless
`AllowPrivateTargets` is on.

### RDAP and WHOIS lookup

**Answers:** who registered this domain or owns this IP range, and when does the
registration expire.

| Field | Accepts |
|---|---|
| Domain or IP (required) | Up to 253 characters. |
| Show raw RDAP response | Off by default. |

**Limits.** RDAP is the structured successor to WHOIS and is what registries are now
required to serve. Personal registrant details are redacted at source under GDPR for
most domains, so expect the registrar and the dates rather than a name and address.
This is the one tool that reaches a third party by design — `rdap.org` for the
bootstrap, then whichever registry it redirects to. Every hop is re-validated.

---

## Security

### TLS certificate checker

**Answers:** what certificate does this host actually serve, when does it expire, and
what did the handshake negotiate.

| Field | Accepts |
|---|---|
| Host (required) | A hostname or a URL — only the host is used. Up to 253 characters. |
| Port | 1–65535, default 443. 465 or 993 for mail, 636 for LDAPS. |
| SNI name | Optional, up to 253 characters. Sends a different server name in the handshake. |

**Limits.** The certificate is read from a real handshake, so what you see is what that
server serves for the name you asked about — including the case where SNI selects a
different certificate, which is exactly what the third field is for. Validation errors
are reported rather than thrown, so an expired or self-signed certificate is still
fully inspectable.

### HTTP security headers

**Answers:** which browser protections is this site missing, and how much does that
matter.

| Field | Accepts |
|---|---|
| URL (required) | Up to 2048 characters. |
| Show all response headers | Off by default. Also surfaces the headers that advertise your stack. |

**Limits.** The grade weights headers by how much they actually change browser
behaviour, so a strong CSP counts for more than a `Permissions-Policy`. It is a starting
point for a hardening conversation, not a compliance verdict — a policy can be present
and still be permissive. Response bodies are capped at `MaxResponseBytes` and redirects
at `MaxRedirects`, each hop re-validated.

### security.txt validator

**Answers:** does this domain publish a usable vulnerability-disclosure contact, and is
it still in date.

| Field | Accepts |
|---|---|
| Domain or URL (required) | Up to 2048 characters. |

**Limits.** RFC 9116 requires the file at `/.well-known/security.txt`, served over
HTTPS, with at least `Contact` and `Expires`. The legacy `/security.txt` location is
checked too, so an older deployment gets a useful migration finding rather than a bare
failure. An expired `Expires` field is a finding in its own right — a stale security.txt
is worse than none, because it promises a channel nobody is reading.

### JWT decoder

**Answers:** what does this token claim, and has it expired.

| Field | Accepts |
|---|---|
| Token (required) | A compact-serialisation JWT, pasted into a textarea. |

**Limits.** **Decoding is not verifying.** This tool reads what the token says; it does
not check the signature, so never treat a decoded claim as proof of anything. Expiry and
not-before are compared against *your browser's* clock, so a wrong local clock makes a
valid token look expired. Runs entirely in the browser — the token is never posted, which
is why this tool is safe for a live Entra ID access token.

### Password generator

**Answers:** give me secrets I can defend the entropy of.

| Field | Accepts |
|---|---|
| Style | Password or passphrase. |
| How many | 1–50, default 5. |
| Length | 4–256, default 20 (password style). |
| Words | 3–12, default 8 (passphrase style). |
| Uppercase / Lowercase / Digits / Symbols | All on by default. |
| Exclude look-alike characters | Off. Drops `I l 1 O 0` — worth it when someone will read the password aloud or type it from a screen. |

**Limits.** Randomness comes from `crypto.getRandomValues`, in your browser. The entropy
figure is calculated from the generator's own choices — alphabet size and length — which
is the honest measure for a random secret and says nothing about a password a human
invented, where the predictability of the pattern matters far more than the character
count.

### Hash generator

**Answers:** what is the digest of this input, and does it match the checksum I was
given.

| Field | Accepts |
|---|---|
| Text (required) | A textarea. |
| Compare with | Optional digest, up to 200 characters. Any algorithm whose output matches is highlighted. |
| Uppercase output | Off by default. |

**Limits.** MD5 and SHA-1 are included because checksums published years ago still use
them, not because they are safe — both are broken for anything needing collision
resistance. Use SHA-256 or better for new work. Everything is computed in the browser,
so hashing a secret here is safe.

---

## Email

### SPF, DKIM and DMARC checker

**Answers:** would this domain's mail authentication actually stop a spoofed message.

| Field | Accepts |
|---|---|
| Domain (required) | Up to 253 characters. |
| Resolver | Optional IP, up to 45 characters. |
| Extra DKIM selectors | Optional, comma-separated, up to 300 characters. |

**Limits.** The SPF ten-lookup limit is evaluated, which is the failure that bites
Microsoft 365 tenants after the third connector gets added. **DKIM cannot be enumerated
from DNS** — a selector is only discoverable from a signed message's `DKIM-Signature`
header. This tool probes the selectors common platforms use, so a domain signing with a
custom selector shows *none found* even though DKIM is working fine. Put yours in the
third field. DNS only: no mail is sent and no message is relayed.

---

## Windows

### Windows error code lookup

**Answers:** what is `0x80070005`, and which component raised it.

| Field | Accepts |
|---|---|
| Error code (required) | Up to 80 characters. HRESULT, Win32, NTSTATUS, MSI, Windows Update, DISM or Intune, hex or decimal. |

**Limits.** Even when a code is not in the dataset, the HRESULT decomposition still
tells you a lot: the facility names the component that raised it, and a `FACILITY_WIN32`
result carries an ordinary Win32 error in its low sixteen bits, which is looked up
automatically. The dataset is a JSON file in the repository, so adding entries is a
documentation-sized pull request rather than a code change. Server side, but entirely
offline — no network access.

---

## Encoding and Ops

### Encoder and decoder

**Answers:** turn this into base64, or back out of it, without a wrong-charset surprise.

| Field | Accepts |
|---|---|
| Input (required) | A textarea. |
| Format | Base64, base64url, percent-encoding, hex or HTML entities. |
| Direction | Encode or decode. |

**Limits.** UTF-8 correct in both directions. **Base64 is an encoding, not encryption** —
anything encoded here is trivially readable by anyone who has it. Kubernetes secrets and
basic-auth headers are base64, which is exactly why they must be treated as plaintext
credentials. Runs in the browser.

### Cron expression parser

**Answers:** what does this cron expression mean, and when does it next fire.

| Field | Accepts |
|---|---|
| Cron expression (required) | Five fields (`minute hour day month weekday`) or six, where the first is seconds. Macros such as `@daily` and `@weekly` work. Up to 200 characters. |
| Time zone | An IANA name like `Europe/Dublin`, or a Windows name. Defaults to UTC. |
| Next runs to show | 1–50, default 10. |

**Limits.** Occurrences are computed in the time zone you pick, so a daylight-saving
transition shows up in the list as a skipped or repeated run — exactly the surprise that
breaks nightly jobs twice a year. Pure arithmetic: nothing leaves the instance.

---

## Adding a tool

A new tool appears here the same way it appears in the gallery: by existing. The
registry discovers it, the form is generated from its `[ToolField]` attributes, and the
`Notes` property it declares is where the honest caveat in its section above comes from.
See [CONTRIBUTING.md](../../CONTRIBUTING.md) for the fifteen-minute walkthrough and
[ARCHITECTURE.md](../../ARCHITECTURE.md) for why it needs no registration.

If you add a tool that fetches a target, take `IOutboundTargetValidator` or
`SafeHttpFetcher` as a constructor dependency rather than an `HttpClient`, and add a row
to the table at the top of this page. A tool that bypasses the guard is a vulnerability,
not a feature.
