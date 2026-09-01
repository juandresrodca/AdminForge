using AdminForge.Core.Tools;

namespace AdminForge.Core.Registry;

/// <summary>
/// Immutable, singleton catalogue built once at startup. Every lookup is an
/// in-memory dictionary hit or a linear scan over a few dozen items, so nothing here
/// needs caching on top.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ToolDescriptor> _byId;
    private readonly Dictionary<ToolCategory, IReadOnlyList<ToolDescriptor>> _byCategory;

    /// <summary>Builds a registry over an already-validated descriptor set.</summary>
    /// <param name="descriptors">Descriptors produced by <see cref="ToolDiscovery.BuildDescriptors"/>.</param>
    public ToolRegistry(IEnumerable<ToolDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        All = descriptors
            .OrderBy(d => d.Category)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _byId = All.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        _byCategory = All
            .GroupBy(d => d.Category)
            .ToDictionary(g => g.Key, IReadOnlyList<ToolDescriptor> (g) => g.ToList());

        Categories = Enum.GetValues<ToolCategory>()
            .Where(_byCategory.ContainsKey)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDescriptor> All { get; }

    /// <inheritdoc />
    public IReadOnlyList<ToolCategory> Categories { get; }

    /// <inheritdoc />
    public ToolDescriptor? Find(string id) =>
        string.IsNullOrWhiteSpace(id) ? null : _byId.GetValueOrDefault(id.Trim());

    /// <inheritdoc />
    public IReadOnlyList<ToolDescriptor> InCategory(ToolCategory category) =>
        _byCategory.GetValueOrDefault(category, []);

    /// <inheritdoc />
    public IReadOnlyList<ToolDescriptor> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All;
        }

        string normalised = query.Trim().ToLowerInvariant();
        return All.Where(d => d.Matches(normalised)).ToList();
    }
}
