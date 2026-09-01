using System.Text.RegularExpressions;
using AdminForge.Core.Forms;
using AdminForge.Core.Registry;
using AdminForge.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.Tests;

/// <summary>
/// The guard rail that makes an external pull request safe to merge.
/// <para>
/// Every rule here is something a reviewer would otherwise have to check by hand on
/// each new tool. If one of these fails, the contribution is not finished — and the
/// failure message says exactly what to fix.
/// </para>
/// </summary>
[Collection(ToolRegistryCollection.Name)]
public sealed class ToolContractTests(ToolRegistryFixture fixture)
{
    private static readonly Regex KebabCase = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    public static TheoryData<string> ToolIds()
    {
        var data = new TheoryData<string>();

        foreach (ToolDescriptor tool in Tools.Value)
        {
            data.Add(tool.Id);
        }

        return data;
    }

    /// <summary>Discovered once, statically, so the theory data can be built without the fixture.</summary>
    private static readonly Lazy<IReadOnlyList<ToolDescriptor>> Tools =
        new(() => new ToolRegistryFixture().Registry.All);

    private ToolDescriptor Tool(string id) =>
        fixture.Registry.Find(id) ?? throw new InvalidOperationException($"Tool '{id}' vanished from the registry.");

    [Fact]
    public void The_registry_discovers_tools()
    {
        Assert.NotEmpty(fixture.Registry.All);
        Assert.NotEmpty(fixture.Registry.Categories);
    }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void Id_is_kebab_case(string id) =>
        Assert.True(
            KebabCase.IsMatch(id),
            $"'{id}' is not kebab-case. Ids appear in URLs, so use lowercase letters, digits and single hyphens.");

    [Fact]
    public void Ids_are_unique()
    {
        var duplicates = fixture.Registry.All
            .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate tool ids: {string.Join(", ", duplicates)}");
    }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void Name_and_description_are_present_and_well_formed(string id)
    {
        ToolDescriptor tool = Tool(id);

        Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"{id}: Name is empty.");
        Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{id}: Description is empty.");

        Assert.False(
            tool.Description.EndsWith('.'),
            $"{id}: the description is a fragment shown on a card — drop the trailing period.");

        Assert.True(
            tool.Description.Length <= 110,
            $"{id}: the description is {tool.Description.Length} characters. Keep it under 110 so it fits a gallery card.");

        Assert.True(
            char.IsUpper(tool.Description[0]),
            $"{id}: start the description with a capital letter.");
    }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void Keywords_are_present_and_lowercase(string id)
    {
        ToolDescriptor tool = Tool(id);

        Assert.True(
            tool.Keywords.Count >= 3,
            $"{id}: add at least three keywords. They are how people find the tool in the search box.");

        foreach (string keyword in tool.Keywords)
        {
            Assert.True(
                keyword == keyword.ToLowerInvariant(),
                $"{id}: keyword '{keyword}' must be lowercase — search lower-cases the query before matching.");
        }
    }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void Icon_exists_in_the_sprite(string id)
    {
        ToolDescriptor tool = Tool(id);
        string sprite = File.ReadAllText(Path.Combine(
            ToolRegistryFixture.RepositoryRoot,
            "src", "AdminForge.Web", "wwwroot", "icons", "sprite.svg"));

        Assert.Contains(
            $"id=\"{tool.Icon}\"",
            sprite,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void Server_tools_have_a_handler_and_client_tools_have_a_module(string id)
    {
        ToolDescriptor tool = Tool(id);

        if (tool.Compute == ComputeMode.ServerSide)
        {
            Assert.NotNull(tool.InputType);
            Assert.Null(tool.ClientScript);
            return;
        }

        Assert.NotNull(tool.ClientScript);

        string module = Path.Combine(
            ToolRegistryFixture.RepositoryRoot,
            "src", "AdminForge.Tools", "wwwroot", "tools", $"{tool.Id}.js");

        Assert.True(
            File.Exists(module),
            $"{id} is client-side, so it needs a browser module at wwwroot/tools/{id}.js.");

        string source = File.ReadAllText(module);

        Assert.True(
            source.Contains("export function run", StringComparison.Ordinal)
            || source.Contains("export async function run", StringComparison.Ordinal),
            $"{id}: the browser module must export a run function.");
    }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void Every_form_field_is_labelled_and_usable(string id)
    {
        ToolDescriptor tool = Tool(id);

        foreach (ToolFieldDescriptor field in tool.Form.Fields)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(field.Label),
                $"{id}.{field.Name}: every field needs a label — a placeholder is not a label.");

            Assert.NotEqual(FieldKind.Auto, field.Kind);

            if (field.Kind == FieldKind.Select)
            {
                Assert.True(
                    field.Options.Count > 0,
                    $"{id}.{field.Name}: a select with no options cannot be used.");
            }

            // A free-text field with no ceiling is a denial-of-service invitation on a
            // public instance, and the binder enforces whatever value is set here.
            if (field.Kind is FieldKind.Text or FieldKind.TextArea or FieldKind.Password)
            {
                Assert.True(
                    field.MaxLength > 0,
                    $"{id}.{field.Name}: set MaxLength on text input so the server can reject oversized submissions.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void Tool_resolves_from_the_container(string id)
    {
        ToolDescriptor tool = Tool(id);
        object instance = fixture.Provider.GetRequiredService(tool.ToolType);

        Assert.IsAssignableFrom<ITool>(instance);
    }

    [Fact]
    public void Search_finds_every_tool_by_its_own_name()
    {
        foreach (ToolDescriptor tool in fixture.Registry.All)
        {
            IReadOnlyList<ToolDescriptor> hits = fixture.Registry.Search(tool.Name);

            Assert.Contains(hits, t => t.Id == tool.Id);
        }
    }

    [Fact]
    public void Search_finds_tools_by_keyword()
    {
        Assert.Contains(fixture.Registry.Search("cidr"), t => t.Id == "subnet-calculator");
        Assert.Contains(fixture.Registry.Search("dmarc"), t => t.Id == "mail-auth-checker");
        Assert.Contains(fixture.Registry.Search("hresult"), t => t.Id == "windows-error-code");
    }

    [Fact]
    public void Find_is_case_insensitive_and_returns_null_for_unknown_ids()
    {
        Assert.NotNull(fixture.Registry.Find("SUBNET-CALCULATOR"));
        Assert.Null(fixture.Registry.Find("no-such-tool"));
        Assert.Null(fixture.Registry.Find(""));
    }
}
