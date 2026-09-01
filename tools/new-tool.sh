#!/usr/bin/env bash
#
# Scaffolds a new AdminForge tool.
#
# Creates the folder, the tool class, the input model and a test file, filled in and
# ready to build. Nothing outside the new folder is touched — no registration step,
# no core file to edit.
#
# Usage:
#   ./tools/new-tool.sh "MAC address lookup" Network
#   ./tools/new-tool.sh "Hash identifier" Security ClientSide hash
#
# Arguments:
#   1  name      Human-readable, e.g. "MAC address lookup".
#   2  category  Network | Security | Windows | Identity | Email | Encoding | Ops
#   3  compute   ServerSide (default) | ClientSide
#   4  icon      Icon id from src/AdminForge.Web/wwwroot/icons/sprite.svg (default: tool)

set -euo pipefail

name="${1:-}"
category="${2:-}"
compute="${3:-ServerSide}"
icon="${4:-tool}"

if [[ -z "$name" || -z "$category" ]]; then
    sed -n '3,22p' "$0" | sed 's/^# \{0,1\}//'
    exit 1
fi

case "$category" in
    Network|Security|Windows|Identity|Email|Encoding|Ops) ;;
    *) echo "Unknown category '$category'. Use Network, Security, Windows, Identity, Email, Encoding or Ops." >&2; exit 1 ;;
esac

case "$compute" in
    ServerSide|ClientSide) ;;
    *) echo "Compute must be ServerSide or ClientSide, not '$compute'." >&2; exit 1 ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tools_root="$repo_root/src/AdminForge.Tools"
tests_root="$repo_root/tests/AdminForge.Tests/Tools"

# "MAC address lookup" -> MacAddressLookup / mac-address-lookup
read -ra words <<< "$(echo "$name" | tr -c '[:alnum:]' ' ')"

if [[ ${#words[@]} -eq 0 ]]; then
    echo "Give the tool a name with at least one letter or digit." >&2
    exit 1
fi

pascal=""
kebab=""
for word in "${words[@]}"; do
    lower="$(echo "$word" | tr '[:upper:]' '[:lower:]')"
    first="$(echo "${lower:0:1}" | tr '[:lower:]' '[:upper:]')"
    pascal+="${first}${lower:1}"
    kebab+="${kebab:+-}${lower}"
done

folder="$tools_root/$category/$pascal"

if [[ -d "$folder" ]]; then
    echo "$folder already exists. Pick another name, or delete it first." >&2
    exit 1
fi

mkdir -p "$folder" "$tests_root"

namespace="AdminForge.Tools.$category.$pascal"
lower_name="$(echo "$name" | tr '[:upper:]' '[:lower:]')"

if [[ "$compute" == "ClientSide" ]]; then
    contract="IClientTool<${pascal}Input>"
    usings="using AdminForge.Core.Tools;"
else
    contract="IToolHandler<${pascal}Input>"
    usings="using System.Globalization;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;"
fi

# ---- input model -------------------------------------------------------------
cat > "$folder/${pascal}Input.cs" <<EOF
using AdminForge.Core.Forms;

namespace $namespace;

/// <summary>Input for the $lower_name.</summary>
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
EOF

# ---- tool class --------------------------------------------------------------
if [[ "$compute" == "ClientSide" ]]; then
    handler_body="    // A client-side tool has no server handler. The work happens in
    // src/AdminForge.Tools/wwwroot/tools/$kebab.js, which never reaches the server."
else
    handler_body='    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync('"${pascal}"'Input input, CancellationToken cancellationToken)
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
            .Status(ResultStatus.Ok, "It worked", $"You entered {input.Value.Length} characters.")
            .KeyValues("Details", kv => kv
                .Add("Input", input.Value, monospace: true)
                .Add("Length", input.Value.Length.ToString(CultureInfo.InvariantCulture), monospace: true))
            .ToResult();

        return Task.FromResult(result);
    }'
fi

cat > "$folder/${pascal}Tool.cs" <<EOF
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
    public string Name => "$name";

    /// <inheritdoc />
    public string Description => "TODO: one sentence, sentence case, no trailing period";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.$category;

    /// <inheritdoc />
    public string Icon => "$icon";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.$compute;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["todo", "add", "keywords"];

    /// <inheritdoc />
    public string? Notes =>
        "Optional. Caveats, RFC references, or why the answer differs from another tool.";

$handler_body
}
EOF

# ---- test --------------------------------------------------------------------
if [[ "$compute" == "ClientSide" ]]; then
    cat > "$tests_root/${pascal}ToolTests.cs" <<EOF
using AdminForge.Core.Tools;
using $namespace;

namespace AdminForge.Tests.Tools;

/// <summary>
/// Metadata for the $lower_name.
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
EOF
else
    cat > "$tests_root/${pascal}ToolTests.cs" <<EOF
using AdminForge.Core.Results;
using $namespace;

namespace AdminForge.Tests.Tools;

/// <summary>Behaviour of the $lower_name.</summary>
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
EOF
fi

created=(
    "src/AdminForge.Tools/$category/$pascal/${pascal}Tool.cs"
    "src/AdminForge.Tools/$category/$pascal/${pascal}Input.cs"
    "tests/AdminForge.Tests/Tools/${pascal}ToolTests.cs"
)

if [[ "$compute" == "ClientSide" ]]; then
    cat > "$tools_root/wwwroot/tools/$kebab.js" <<EOF
import { ok, fail, status, keyValues } from "./blocks.js";

/**
 * $name — runs entirely in the browser.
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
EOF
    created+=("src/AdminForge.Tools/wwwroot/tools/$kebab.js")
fi

echo
echo "Created $pascal ($kebab)"
printf '  + %s\n' "${created[@]}"
echo
echo "Next:"
echo "  1. Fill in Description, Keywords and the TODOs."
echo "  2. dotnet run --project src/AdminForge.Web    then open http://localhost:5099/tools/$kebab"
echo "  3. dotnet test"
if [[ "$compute" == "ClientSide" ]]; then
    echo "  4. node --test \"tests/js/*.test.mjs\""
fi
echo
