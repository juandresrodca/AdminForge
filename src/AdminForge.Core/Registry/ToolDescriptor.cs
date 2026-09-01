using AdminForge.Core.Forms;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Core.Registry;

/// <summary>
/// Everything the core knows about one tool, resolved once at startup: its metadata,
/// its generated form, and a pre-bound delegate for invoking its handler.
/// <para>
/// This is a snapshot. It holds no tool instance — a fresh one is resolved from the
/// request scope on every execution, so tools may safely take scoped dependencies.
/// </para>
/// </summary>
public sealed class ToolDescriptor
{
    internal ToolDescriptor(ITool tool, Type toolType, ToolFormDescriptor form, IHandlerInvoker? invoker)
    {
        Id = tool.Id;
        Name = tool.Name;
        Description = tool.Description;
        Category = tool.Category;
        Icon = tool.Icon;
        Compute = tool.Compute;
        Keywords = tool.Keywords;
        Notes = tool.Notes;
        ToolType = toolType;
        Form = form;
        Invoker = invoker;

        _searchText = string.Join(
            ' ',
            new[] { Name, Description, Category.ToString(), Id.Replace('-', ' ') }
                .Concat(Keywords))
            .ToLowerInvariant();
    }

    private readonly string _searchText;

    /// <summary>Kebab-case id; also the URL segment.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>One-line description.</summary>
    public string Description { get; }

    /// <summary>Gallery grouping.</summary>
    public ToolCategory Category { get; }

    /// <summary>Sprite icon id.</summary>
    public string Icon { get; }

    /// <summary>Client-side or server-side execution.</summary>
    public ComputeMode Compute { get; }

    /// <summary>Extra search terms.</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>Optional long-form notes.</summary>
    public string? Notes { get; }

    /// <summary>The concrete tool type, resolved per request from DI.</summary>
    public Type ToolType { get; }

    /// <summary>The generated form.</summary>
    public ToolFormDescriptor Form { get; }

    /// <summary>Null for client-side tools.</summary>
    internal IHandlerInvoker? Invoker { get; }

    /// <summary>The tool's input model type, or null when it has none.</summary>
    public Type? InputType => Form.InputType;

    /// <summary>Relative URL of this tool's page.</summary>
    public string Url => $"/tools/{Id}";

    /// <summary>
    /// Path to the browser module backing a client-side tool. Static web assets from
    /// the tools library are served under the <c>_content</c> prefix.
    /// </summary>
    public string? ClientScript => Compute == ComputeMode.ClientSide
        ? $"/_content/AdminForge.Tools/tools/{Id}.js"
        : null;

    /// <summary>Case-insensitive substring match across name, description, id and keywords.</summary>
    /// <param name="lowercaseQuery">A query that has already been lower-cased and trimmed.</param>
    public bool Matches(string lowercaseQuery) => _searchText.Contains(lowercaseQuery, StringComparison.Ordinal);

    /// <summary>Invoke the tool's handler against a bound input model.</summary>
    /// <param name="toolInstance">A freshly resolved instance of <see cref="ToolType"/>.</param>
    /// <param name="input">An instance of <see cref="InputType"/>.</param>
    /// <param name="cancellationToken">Cancellation for the request.</param>
    public Task<ToolResult> ExecuteAsync(object toolInstance, object input, CancellationToken cancellationToken)
    {
        if (Invoker is null)
        {
            throw new InvalidOperationException(
                $"Tool '{Id}' is client-side and has no server handler to invoke.");
        }

        return Invoker.InvokeAsync(toolInstance, input, cancellationToken);
    }
}
