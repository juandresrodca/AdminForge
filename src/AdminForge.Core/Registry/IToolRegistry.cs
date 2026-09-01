using AdminForge.Core.Tools;

namespace AdminForge.Core.Registry;

/// <summary>
/// The catalogue of every tool discovered at startup. Injected wherever the app needs
/// to list, find or execute a tool.
/// </summary>
public interface IToolRegistry
{
    /// <summary>Every tool, ordered by category then name.</summary>
    IReadOnlyList<ToolDescriptor> All { get; }

    /// <summary>Categories that actually contain at least one tool, in enum order.</summary>
    IReadOnlyList<ToolCategory> Categories { get; }

    /// <summary>Look a tool up by its id. Ids are matched case-insensitively.</summary>
    ToolDescriptor? Find(string id);

    /// <summary>Tools in one category, ordered by name.</summary>
    IReadOnlyList<ToolDescriptor> InCategory(ToolCategory category);

    /// <summary>
    /// Free-text search across name, description, id and keywords. An empty or
    /// whitespace query returns <see cref="All"/>.
    /// </summary>
    IReadOnlyList<ToolDescriptor> Search(string? query);
}
