using AdminForge.Core.Tools;

namespace AdminForge.Core.Registry;

/// <summary>Display metadata for a <see cref="ToolCategory"/>.</summary>
/// <param name="Label">The name shown in the gallery.</param>
/// <param name="Slug">The lowercase URL fragment.</param>
/// <param name="Icon">Sprite icon id used on the category header.</param>
/// <param name="Blurb">One line describing what belongs in the category.</param>
public readonly record struct ToolCategoryInfo(string Label, string Slug, string Icon, string Blurb);

/// <summary>Presentation details for the tool categories.</summary>
public static class ToolCategories
{
    private static readonly Dictionary<ToolCategory, ToolCategoryInfo> Map = new()
    {
        [ToolCategory.Network] = new("Network", "network", "network",
            "Addressing, name resolution and reachability"),
        [ToolCategory.Security] = new("Security", "security", "shield",
            "Certificates, hashes, tokens and hardening checks"),
        [ToolCategory.Windows] = new("Windows", "windows", "windows",
            "Error codes, event IDs and Windows-specific lookups"),
        [ToolCategory.Identity] = new("Identity", "identity", "key",
            "Entra ID, Active Directory, LDAP and federation"),
        [ToolCategory.Email] = new("Email", "email", "mail",
            "Mail flow, deliverability and message headers"),
        [ToolCategory.Encoding] = new("Encoding", "encoding", "code",
            "Encoding, serialisation and text transforms"),
        [ToolCategory.Ops] = new("Ops", "ops", "gauge",
            "Capacity, scheduling, SLAs and unit maths"),
    };

    /// <summary>Display metadata for a category.</summary>
    /// <param name="category">The category.</param>
    public static ToolCategoryInfo Info(this ToolCategory category) =>
        Map.TryGetValue(category, out ToolCategoryInfo info)
            ? info
            : new ToolCategoryInfo(category.ToString(), category.ToString().ToLowerInvariant(), "tool", string.Empty);

    /// <summary>Resolve a category from its URL slug.</summary>
    /// <param name="slug">The slug, case-insensitive.</param>
    public static ToolCategory? FromSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        foreach ((ToolCategory category, ToolCategoryInfo info) in Map)
        {
            if (string.Equals(info.Slug, slug, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return null;
    }
}
