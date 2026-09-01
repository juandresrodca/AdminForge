# Architecture

AdminForge is an ASP.NET Core MVC application built around one constraint: **adding a
tool must not touch the core.** Everything below follows from that.

If you only read one section, read [The contract](#the-contract) and
[Where things go](#where-things-go).

---

## The shape of it

```
                         ┌──────────────────────────┐
   browser ──────────────►  AdminForge.Web          │
                         │  gallery · routing · CSP │
                         └────────────┬─────────────┘
                                      │ asks
                         ┌────────────▼─────────────┐
                         │  AdminForge.Core         │
                         │  ITool · registry · forms│
                         │  results · SSRF guard    │
                         └────────────┬─────────────┘
                                      │ discovers by reflection
                         ┌────────────▼─────────────┐
                         │  AdminForge.Tools        │
                         │  one folder per tool     │  ← contributions land here
                         └──────────────────────────┘
```

Three projects, one direction of dependency. `Core` knows nothing about any specific
tool; `Web` knows nothing about them either. Neither has a list to add to.

---

## The contract

Every tool implements `ITool`, which is metadata only:

```csharp
public interface ITool
{
    string       Id          { get; }   // kebab-case; becomes the URL
    string       Name        { get; }
    string       Description { get; }
    ToolCategory Category    { get; }
    string       Icon        { get; }   // id in the shared SVG sprite
    ComputeMode  Compute     { get; }   // ClientSide | ServerSide
    IReadOnlyList<string> Keywords => [];
    string?      Notes       => null;
}
```

Tools that do work on the server also implement:

```csharp
public interface IToolHandler<in TInput> where TInput : class
{
    Task<ToolResult> ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
```

Tools that run in the browser declare their form shape through the mirror image, which
carries no execution method:

```csharp
public interface IClientTool<TInput> where TInput : class;
```

`TInput` is a plain class whose properties carry `[ToolField]`. That single model is
the form, the binding target and the validation source, so a tool cannot drift into
accepting something the UI never offered.

---

## Why a tool needs no view and no markup

Two decisions do most of the work.

### Forms are declared

`ToolFormFactory` reads `[ToolField]` off the input model once at startup and produces
a `ToolFormDescriptor`. `Views/Shared/_ToolForm.cshtml` renders that descriptor. A
contributor describes the input; the core renders a themed, labelled, accessible form.

### Results are structured

A handler returns `ToolResult`, which is a list of `ResultBlock`s — `StatusBlock`,
`KeyValueBlock`, `TableBlock`, `CodeBlock`, `ListBlock`, `TextBlock`. The core renders
them in `_ResultBlock.cshtml`.

That means new tools automatically look right, and reviewing a contribution is about
its logic rather than its HTML. It also means the visual design can change once, for
every tool at the same time.

Client-side tools produce the **same block shapes** in JavaScript, and
`wwwroot/js/adminforge.js` renders them with a mirror of the Razor partial. One
contract, two renderers — the duplication is deliberate and small, and it is what lets
a browser-only tool look identical to a server one.

### The escape hatch

`AdminForge.Tools` is a Razor Class Library. A tool that genuinely needs bespoke UI can
add `Views/Tools/{tool-id}/Form.cshtml` and the core will use it instead of the
generated form. In practice almost nothing needs this, and a pull request that reaches
for it should say why.

---

## Discovery and dispatch

At startup, `ServiceCollectionExtensions.AddAdminForge` calls:

1. **`ToolDiscovery.FindToolTypes`** — reflects over the tools assembly for concrete
   `ITool` implementations.
2. **`services.AddScoped(toolType)`** for each, so a tool may take any dependency a
   request can.
3. **`ToolDiscovery.BuildDescriptors`** — instantiates each tool inside a throwaway
   scope, validates it, and snapshots its metadata into a `ToolDescriptor`.

`BuildDescriptors` **fails the whole startup** if any tool breaks the contract, and it
reports every problem at once rather than the first:

```
AdminForge found 2 problems with the registered tools:
  - MacLookupTool: Id 'MAC_Lookup' is not kebab-case. Use lowercase letters, digits
    and single hyphens, e.g. 'subnet-calculator'.
  - PingTool: Compute is ServerSide but the class does not implement
    IToolHandler<TInput>. Either implement it or mark the tool ClientSide.
```

A contributor sees that on their first `dotnet run` rather than shipping a tool that
silently never appears. `ToolContractTests` asserts the same rules plus a few more the
registry cannot see — that the icon exists in the sprite, that a client-side tool has a
browser module, that text fields have a `MaxLength` — so a broken contribution fails CI
instead of reaching `main`.

Each descriptor holds a pre-bound `IHandlerInvoker`, a tiny closed generic built once
via `MakeGenericType`. Dispatching a request costs one interface call and two casts;
there is no reflection on the hot path.

The descriptor is a snapshot and holds no tool instance. A fresh instance is resolved
from the request scope on every execution, so a field on a tool cannot leak between two
users' runs.

---

## Request flow

```
GET  /                → HomeController.Index    gallery, search, category filter
GET  /tools/{id}      → ToolsController.Details  the tool page
POST /tools/{id}      → ToolsController.Execute  bind, validate, run, render
GET  /healthz         → liveness probe
```

One controller serves every tool. Adding one needs no route and no controller.

`Execute` binds the posted form to the input model through `ToolInputBinder`, using the
same descriptor that rendered the form. It then resolves the tool, runs it under a
linked cancellation token carrying the configured timeout, and renders the result.

**Progressive enhancement.** A plain form post returns the whole page and works with
JavaScript disabled. When JavaScript is on, `adminforge.js` intercepts the submit,
posts with an `X-AdminForge-Partial` header, and swaps in just the result fragment.
Same controller action, same markup, two delivery modes.

---

## Security

Half the tools take a target from the user and make an outbound request. That is
server-side request forgery by design, so the mitigation lives in the core and every
tool inherits it rather than reimplementing it.

**`IOutboundTargetValidator`** resolves the host and refuses it if any resolved address
is loopback, RFC 1918 private, link-local (which covers cloud instance metadata at
169.254.169.254), carrier-grade NAT, unique-local, multicast, or documentation space.
It refuses a name that resolves to a mix of public and private addresses, because that
is a DNS-rebinding vector. `AdminForge:AllowPrivateTargets` lets a homelab operator opt
in deliberately — that switch is the difference between a useful internal instance and
an open proxy, and it is off by default.

**`SafeHttpFetcher`** handles redirects itself so every hop is re-validated: a
permitted public URL is otherwise free to redirect to an internal one.

**The connect callback** on the shared `SocketsHttpHandler` resolves the target again
immediately before connecting and refuses private addresses at the socket. That closes
the window between the DNS check and the connection.

Beyond that: a strict content security policy with no inline script or style and no
third-party origins, a per-client fixed-window rate limiter on tool execution, and no
logging of tool input.

**AdminForge makes no third-party requests of its own.** No CDN, no analytics, no
telemetry, no web fonts. Every asset is served from the container, so it works in an
air-gapped network — which for this audience is a feature, not an accident.

---

## Where things go

```
AdminForge/
├─ src/
│  ├─ AdminForge.Core/                  contributors never edit this
│  │  ├─ Tools/       ITool · IToolHandler · IClientTool · enums
│  │  ├─ Forms/       ToolFieldAttribute · FieldKind · ToolFormFactory
│  │  ├─ Results/     ToolResult · ResultBlock · ResultBuilder
│  │  ├─ Net/         IpAddressRules · OutboundTargetValidator · SafeHttpFetcher
│  │  ├─ Registry/    ToolDiscovery · ToolRegistry · ToolDescriptor
│  │  └─ Configuration/ AdminForgeOptions
│  │
│  ├─ AdminForge.Tools/                 almost every contribution lands here
│  │  ├─ Network/SubnetCalculator/      one folder per tool
│  │  ├─ Security/TlsCertificate/
│  │  ├─ Windows/ErrorCode/             + windows-error-codes.json
│  │  └─ wwwroot/tools/{id}.js          browser modules + blocks.js
│  │
│  └─ AdminForge.Web/                   the shell
│     ├─ Controllers/  HomeController · ToolsController · ErrorController
│     ├─ Infrastructure/ ToolInputBinder · SecurityHeadersMiddleware
│     ├─ Views/Shared/ _Layout · _ToolCard · _ToolForm · _ToolOutcome · _ResultBlock
│     └─ wwwroot/      css/adminforge.css · js/adminforge.js · icons/sprite.svg
│
├─ tests/
│  ├─ AdminForge.Tests/                 xUnit — contract tests plus per-tool tests
│  └─ js/tools.test.mjs                 node --test, no dependencies
│
└─ tools/new-tool.ps1 · new-tool.sh     the scaffold
```

**The rule of thumb:** if a change touches `AdminForge.Core`, it is a change to the
platform and deserves a conversation. If it only adds a folder under
`AdminForge.Tools`, it is a tool and should be easy to merge.

---

## Deliberate non-goals

- **No database, no state.** Every tool is a pure function of its input. This is what
  makes the container disposable and the security story short.
- **No accounts.** Nothing to authenticate means nothing to breach. Put it behind your
  reverse proxy's auth if you need it.
- **No API keys.** A tool that needs one is a tool that needs configuration, secrets
  management and a failure mode when the key expires.
- **No plugin loading at runtime.** Tools are compiled in. Loading arbitrary assemblies
  into a security tool is a bad trade.
- **No JavaScript build step.** No bundler, no `node_modules` for the app itself. The
  browser modules are plain ES modules using platform APIs, which is why Node can test
  them unchanged and why a contributor needs only the .NET SDK.

---

## Configuration

Bound from the `AdminForge` section — `appsettings.json`, or `AdminForge__*`
environment variables in Docker.

| Setting | Default | What it does |
|---|---|---|
| `AllowPrivateTargets` | `false` | Lets server-side tools reach private and loopback addresses. Homelab only. |
| `ToolTimeoutSeconds` | `15` | Wall-clock budget for one tool run. |
| `MaxResponseBytes` | `2097152` | Ceiling on any fetched response body. |
| `MaxRedirects` | `5` | Redirects followed, each re-validated. |
| `BlockedHosts` | `[]` | Extra hosts to refuse. |
| `InstanceBanner` | `null` | Notice shown on every page. |
| `RateLimit:*` | 30 per 60s | Per-client throttle on tool execution. |

Invalid values fail at startup rather than at the first request.
