# Contributing to AdminForge

The whole architecture exists to make one thing easy: **adding a tool without touching
anything else**. A typical contribution is three new files in one new folder, and zero
modified files.

If that turns out not to be true for something you want to build, that is a bug in the
architecture and worth [an issue](https://github.com/juandresrodca/AdminForge/issues/new).

---

## Add a tool in 15 minutes

### 0. Get it running (3 minutes)

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Nothing else — no
Node, no bundler, no database.

```bash
git clone https://github.com/juandresrodca/AdminForge.git
cd AdminForge
dotnet run --project src/AdminForge.Web
```

Open <http://localhost:5099>. Editing a `.cshtml` and refreshing works without a
restart; changing C# needs one.

### 1. Claim an issue (1 minute)

Browse the [`good first issue`](https://github.com/juandresrodca/AdminForge/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
list and comment on the one you want. It gets assigned to you, so nobody duplicates
your work. If you have an idea that is not listed, open a
[tool proposal](https://github.com/juandresrodca/AdminForge/issues/new?template=new_tool.yml)
first — it takes a minute and saves you building something that does not fit.

### 2. Scaffold it (1 minute)

```powershell
./tools/new-tool.ps1 -Name "MAC address lookup" -Category Network
```

```bash
./tools/new-tool.sh "MAC address lookup" Network
```

Either script creates:

```
src/AdminForge.Tools/Network/MacAddressLookup/
    MacAddressLookupTool.cs      the metadata and the handler
    MacAddressLookupInput.cs     the input model, which is also the form
tests/AdminForge.Tests/Tools/
    MacAddressLookupToolTests.cs a passing test to grow from
```

Add `-Compute ClientSide` (or a third argument of `ClientSide`) for a tool that must
not send its input anywhere; that also writes `wwwroot/tools/<id>.js`.

The scaffolded tool already builds, already appears in the gallery, and already passes
CI. Everything from here is filling it in.

### 3. Describe the inputs (2 minutes)

You never write HTML. The form is generated from attributes on the input model:

```csharp
public sealed class MacAddressLookupInput
{
    [ToolField("MAC address",
        Placeholder = "00:1A:2B:3C:4D:5E",
        Required = true,
        MaxLength = 64,
        Help = "Colons, hyphens, dots or nothing at all — any common notation works.")]
    public string Address { get; set; } = string.Empty;

    [ToolField("Also show the raw OUI record", Half = true)]
    public bool ShowRaw { get; set; }
}
```

`Kind` is inferred from the property type — `bool` becomes a checkbox, an `enum` becomes
a select, numeric types become a spinner — and you can override it with
`Kind = FieldKind.TextArea` or `FieldKind.Password`.

### 4. Write the logic (5 minutes)

```csharp
public Task<ToolResult> ExecuteAsync(MacAddressLookupInput input, CancellationToken cancellationToken)
{
    if (!MacAddress.TryParse(input.Address, out MacAddress mac))
    {
        return Task.FromResult(ToolResult.Fail(
            "That is not a MAC address. Try something like 00:1A:2B:3C:4D:5E."));
    }

    return Task.FromResult(ToolResult.Build()
        .Status(ResultStatus.Ok, vendor.Name, $"OUI {mac.Oui} · registered {vendor.Registered:yyyy-MM-dd}")
        .KeyValues("Address", kv => kv
            .Add("Normalised", mac.ToString(), monospace: true)
            .Add("OUI", mac.Oui, monospace: true)
            .Add("Locally administered", mac.IsLocal ? "yes" : "no")
            .Add("Multicast", mac.IsMulticast ? "yes" : "no"))
        .ToResult());
}
```

Two rules:

- **`ToolResult.Fail` for expected problems.** Bad input, no records, an unreachable
  host. Write the message for an administrator who mistyped something.
- **Let unexpected exceptions propagate.** The core catches them, logs them with your
  tool's id, and shows a generic message. Do not swallow a bug into a friendly string.

You never write markup for results either. You return blocks — `Status`, `KeyValues`,
`Table`, `Code`, `List`, `Text` — and the core renders them, which is why every tool
looks consistent without its author touching CSS.

### 5. Test it (3 minutes)

Fill in the scaffolded test with a case that works and a case that does not:

```csharp
[Theory]
[InlineData("00:1A:2B:3C:4D:5E")]
[InlineData("001A.2B3C.4D5E")]
[InlineData("00-1A-2B-3C-4D-5E")]
public async Task Accepts_every_common_notation(string address)
{
    ToolResult result = await RunAsync(address);
    Assert.True(result.Succeeded, result.Error);
}

[Fact]
public async Task Explains_itself_when_the_address_is_nonsense()
{
    ToolResult result = await RunAsync("hello");
    Assert.False(result.Succeeded);
}
```

Then:

```bash
dotnet test
dotnet format
```

### 6. Look at it

```bash
dotnet run --project src/AdminForge.Web
```

Open your tool, run it, and check the result reads well. This is the step people skip
and reviewers notice.

### 7. Open the pull request

The template has a checklist matching what CI enforces. Green CI means the mechanical
review is already done, so the conversation can be about the logic.

---

## Client-side or server-side?

This is the only architectural decision a tool author makes, and the answer is not
about convenience.

**`ClientSide`** — the input never leaves the browser, and no request is made at all.
Use it whenever the input could conceivably be a secret: tokens, passwords, private
keys, certificates with a key attached, anything a user might paste from a password
manager. Also use it for pure text transformation, where a round trip buys nothing.

**`ServerSide`** — the input is posted to the AdminForge server. Use it when the tool
needs outbound network access, or a dataset too large to ship to every visitor.

The choice is shown to the user as a badge on the tool page, because "does my secret
leave this machine?" is the first thing a security-minded admin wants to know. Getting
it wrong is the one review comment guaranteed to block a merge.

A client-side tool exports a `run` function from `wwwroot/tools/<id>.js` and builds
results with the same block shapes as the server:

```js
import { ok, fail, status, keyValues } from "./blocks.js";

export function run(input) {
    if (!input.Value) {
        return fail("Paste a value first.");
    }

    return ok(
        status("Ok", "Looks good"),
        keyValues(null, [["Length", input.Value.length, { monospace: true }]]));
}
```

Test those in `tests/js/tools.test.mjs` and run them with:

```bash
node --test "tests/js/*.test.mjs"
```

No bundler, no dependencies — the modules use only platform APIs, so Node runs them
unchanged.

---

## What makes a good AdminForge tool

AdminForge is tilted at **sysadmin, IT and security** work. There are already excellent
toolboxes for web developers; this is not trying to be one.

**Good fits**

- Something you would reach for during an incident, a migration or a change window
- Something that saves opening a terminal, an RFC and a vendor knowledge-base article
- Lookups where the interesting part is the interpretation, not just the raw value —
  the certificate checker earns its place by saying "expires in 12 days", not by
  dumping a certificate

**Poor fits**

- Anything that needs an API key, a login, or per-user state
- Anything requiring a database — AdminForge is stateless by design
- Front-end conveniences with no sysadmin angle
- A thin wrapper over a third-party API that does the actual work

If you are unsure, open a proposal and ask. A five-minute conversation beats a rejected
pull request.

---

## Things the core already gives you

Do not reimplement these — a tool that does will be asked to use them instead.

| You need | Use | Why |
|---|---|---|
| To contact a user-supplied host | `IOutboundTargetValidator` | Refuses private, loopback, link-local and cloud-metadata addresses unless the operator opted in |
| To fetch a user-supplied URL | `SafeHttpFetcher` | Vets the target, re-validates every redirect, caps the body, refuses private addresses at the socket |
| To classify an IP address | `IpAddressRules.IsPrivateOrReserved` | One implementation, one set of tests |
| Deployment settings | `IOptionsMonitor<AdminForgeOptions>` | Timeouts, size caps and the private-target switch |
| To render anything | `ToolResult.Build()` | Consistent output, no markup |

Anything that takes a hostname or URL from the user **must** go through the validator.
A public AdminForge instance would otherwise be an open proxy into whatever network it
runs on. This is the one rule with no exceptions.

---

## Other ways to help

Not every contribution is a new tool.

- **Extend the Windows error-code dataset.** It lives in
  `src/AdminForge.Tools/Windows/ErrorCode/windows-error-codes.json` — adding entries is
  a pull request with no C# in it. Keep it sorted by code; a test enforces that.
- **Expand the passphrase wordlist.** The password generator uses a 256-word list
  (8 bits per word). Swapping in the EFF long list would take it to 12.9.
- **Add DKIM selectors** that real platforms use, in `MailAuthTool.CommonSelectors`.
- **Improve a tool's `Notes`.** The caveat that saves someone an hour is worth as much
  as the tool.
- **Report a wrong answer.** A tool giving confidently incorrect output is the worst
  bug this project can have.
- **Translations, accessibility fixes, documentation.** All welcome.

---

## Style

`.editorconfig` is the source of truth and `dotnet format` enforces it in CI, so there
is no house style to memorise. Beyond that:

- Comments explain **why**, not what. The code already says what.
- Name things the way an admin would say them out loud.
- Every public type and member gets an XML doc comment.
- No new dependency without a reason in the pull request description.

## Code of conduct

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).

## Licence

Contributions are licensed under the [MIT Licence](LICENSE), the same as the project.
