<#
.SYNOPSIS
    Scaffolds a new AdminForge tool.

.DESCRIPTION
    Creates the folder, the tool class, the input model and a test file, filled in and
    ready to build. Nothing outside the new folder is touched — no registration step,
    no core file to edit.

.PARAMETER Name
    Human-readable tool name, e.g. "MAC address lookup". The class names and the
    kebab-case id are derived from it.

.PARAMETER Category
    Network, Security, Windows, Identity, Email, Encoding or Ops.

.PARAMETER Compute
    ServerSide (default) for anything that needs the network or a large dataset;
    ClientSide for anything touching secrets, which also scaffolds the browser module.

.PARAMETER Icon
    Icon id from src/AdminForge.Web/wwwroot/icons/sprite.svg. Defaults to "tool".

.EXAMPLE
    ./tools/new-tool.ps1 -Name "MAC address lookup" -Category Network

.EXAMPLE
    ./tools/new-tool.ps1 -Name "Hash identifier" -Category Security -Compute ClientSide -Icon hash
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Name,

    [Parameter(Mandatory)]
    [ValidateSet('Network', 'Security', 'Windows', 'Identity', 'Email', 'Encoding', 'Ops')]
    [string] $Category,

    [ValidateSet('ServerSide', 'ClientSide')]
    [string] $Compute = 'ServerSide',

    [string] $Icon = 'tool'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot 'src/AdminForge.Tools'
$testsRoot = Join-Path $repoRoot 'tests/AdminForge.Tests/Tools'

# "MAC address lookup" -> MacAddressLookup / mac-address-lookup
$words = $Name -split '[^A-Za-z0-9]+' | Where-Object { $_ }
if (-not $words) { throw "Give the tool a name with at least one letter or digit." }

$pascal = ($words | ForEach-Object {
    $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1).ToLowerInvariant()
}) -join ''
$kebab = ($words | ForEach-Object { $_.ToLowerInvariant() }) -join '-'

$folder = Join-Path $toolsRoot "$Category/$pascal"
if (Test-Path $folder) { throw "$folder already exists. Pick another name, or delete it first." }

New-Item -ItemType Directory -Path $folder -Force | Out-Null
New-Item -ItemType Directory -Path $testsRoot -Force | Out-Null

$namespace = "AdminForge.Tools.$Category.$pascal"
$isClient = $Compute -eq 'ClientSide'
$contract = if ($isClient) { "IClientTool<${pascal}Input>" } else { "IToolHandler<${pascal}Input>" }

# ---- input model -------------------------------------------------------------
$inputSource = @"
using AdminForge.Core.Forms;

namespace $namespace;

/// <summary>Input for the $($Name.ToLowerInvariant()).</summary>
public sealed class ${pascal}Input
{
    /// <summary>The value the tool works on.</summary>
    [ToolField("Value",
        Placeholder = "something realistic",
        Required = true,
        MaxLength = 500,
        Help = "One line explaining what to put here.")]
    public string Value { get; set; } = string.Empty;

    // Add more fields by adding more properties. The form is generated from these
    // attributes — you never write HTML. Kind is inferred from the property type:
    // bool becomes a checkbox, an enum becomes a select, numeric becomes a spinner.
    //
    // [ToolField("Mode", Kind = FieldKind.Select, Options = "fast,thorough", Half = true)]
    // public string Mode { get; set; } = "fast";
}
"@

# ---- tool class --------------------------------------------------------------
$handlerBody = if ($isClient) {
@"
    // A client-side tool has no server handler. The work happens in
    // src/AdminForge.Tools/wwwroot/tools/$kebab.js, which never reaches the server.
"@
} else {
@"
    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(${pascal}Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Return Fail for expected, user-facing problems. Let genuinely unexpected
        // exceptions propagate — the core logs them and shows a generic error.
        if (string.IsNullOrWhiteSpace(input.Value))
        {
            return Task.FromResult(ToolResult.Fail("Enter a value first."));
        }

        // Build the result from blocks. The core renders them, so this tool needs no
        // markup and automatically matches every other tool in the app.
        ToolResult result = ToolResult.Build()
            .Status(ResultStatus.Ok, "It worked", `$"You entered {input.Value.Length} characters.")
            .KeyValues("Details", kv => kv
                .Add("Input", input.Value, monospace: true)
                .Add("Length", input.Value.Length.ToString(CultureInfo.InvariantCulture), monospace: true))
            .ToResult();

        return Task.FromResult(result);
    }
"@
}

$usings = if ($isClient) {
    "using AdminForge.Core.Tools;"
} else {
    "using System.Globalization;`nusing AdminForge.Core.Results;`nusing AdminForge.Core.Tools;"
}

$toolSource = @"
$usings

namespace $namespace;

/// <summary>
/// TODO: one paragraph on what this tool does and why someone reaches for it.
/// </summary>
public sealed class ${pascal}Tool : ITool, $contract
{
    /// <inheritdoc />
    public string Id => "$kebab";

    /// <inheritdoc />
    public string Name => "$Name";

    /// <inheritdoc />
    public string Description => "TODO: one sentence, sentence case, no trailing period";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.$Category;

    /// <inheritdoc />
    public string Icon => "$Icon";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.$Compute;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["todo", "add", "keywords"];

    /// <inheritdoc />
    public string? Notes =>
        "Optional. Caveats, RFC references, or why the answer differs from another tool.";

$handlerBody
}
"@

# ---- test --------------------------------------------------------------------
$testSource = if ($isClient) {
@"
using AdminForge.Core.Tools;
using $namespace;

namespace AdminForge.Tests.Tools;

/// <summary>
/// Metadata for the $($Name.ToLowerInvariant()).
/// <para>
/// The behaviour of a client-side tool is tested in JavaScript — add cases to
/// tests/js/tools.test.mjs and run them with: node --test "tests/js/*.test.mjs"
/// </para>
/// </summary>
public sealed class ${pascal}ToolTests
{
    private static readonly ${pascal}Tool Tool = new();

    [Fact]
    public void Declares_itself_as_a_browser_tool()
    {
        Assert.Equal("$kebab", Tool.Id);
        Assert.Equal(ComputeMode.ClientSide, Tool.Compute);
    }
}
"@
} else {
@"
using AdminForge.Core.Results;
using $namespace;

namespace AdminForge.Tests.Tools;

/// <summary>Behaviour of the $($Name.ToLowerInvariant()).</summary>
public sealed class ${pascal}ToolTests
{
    private static readonly ${pascal}Tool Tool = new();

    private static Task<ToolResult> RunAsync(string value) =>
        Tool.ExecuteAsync(new ${pascal}Input { Value = value }, CancellationToken.None);

    [Fact]
    public async Task Produces_a_result_for_valid_input()
    {
        ToolResult result = await RunAsync("hello");

        Assert.True(result.Succeeded, result.Error);
        Assert.NotEmpty(result.Blocks);
    }

    [Fact]
    public async Task Explains_itself_when_the_input_is_empty()
    {
        ToolResult result = await RunAsync("   ");

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
"@
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $folder "${pascal}Input.cs"), $inputSource, $utf8)
[System.IO.File]::WriteAllText((Join-Path $folder "${pascal}Tool.cs"), $toolSource, $utf8)
[System.IO.File]::WriteAllText((Join-Path $testsRoot "${pascal}ToolTests.cs"), $testSource, $utf8)

$created = @(
    "src/AdminForge.Tools/$Category/$pascal/${pascal}Tool.cs"
    "src/AdminForge.Tools/$Category/$pascal/${pascal}Input.cs"
    "tests/AdminForge.Tests/Tools/${pascal}ToolTests.cs"
)

if ($isClient) {
    $moduleSource = @"
import { ok, fail, status, keyValues } from "./blocks.js";

/**
 * $Name — runs entirely in the browser.
 *
 * @param {object} input  one property per [ToolField] on ${pascal}Input
 * @returns a result built with the helpers from blocks.js
 */
export function run(input) {
    const value = (input.Value || "").trim();

    if (!value) {
        return fail("Enter a value first.");
    }

    return ok(
        status("Ok", "It worked", value.length + " characters in."),
        keyValues("Details", [
            ["Input", value, { monospace: true }],
            ["Length", value.length, { monospace: true }]
        ])
    );
}
"@
    $modulePath = Join-Path $toolsRoot "wwwroot/tools/$kebab.js"
    [System.IO.File]::WriteAllText($modulePath, $moduleSource, $utf8)
    $created += "src/AdminForge.Tools/wwwroot/tools/$kebab.js"
}

Write-Host ""
Write-Host "Created $pascal ($kebab)" -ForegroundColor Green
$created | ForEach-Object { Write-Host "  + $_" }
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  1. Fill in Description, Keywords and the TODOs."
Write-Host "  2. dotnet run --project src/AdminForge.Web    then open http://localhost:5099/tools/$kebab"
Write-Host "  3. dotnet test"
if ($isClient) {
    Write-Host "  4. node --test `"tests/js/*.test.mjs`""
}
Write-Host ""
