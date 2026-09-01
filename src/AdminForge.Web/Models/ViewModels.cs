using AdminForge.Core.Registry;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Web.Models;

/// <summary>Backing model for the tool gallery on the home page.</summary>
/// <param name="Tools">The tools matching the current filter.</param>
/// <param name="TotalCount">How many tools exist in total, regardless of filter.</param>
/// <param name="Query">The active search text, if any.</param>
/// <param name="Category">The active category filter, if any.</param>
/// <param name="Categories">Categories that contain at least one tool.</param>
public sealed record GalleryViewModel(
    IReadOnlyList<ToolDescriptor> Tools,
    int TotalCount,
    string? Query,
    ToolCategory? Category,
    IReadOnlyList<ToolCategory> Categories)
{
    /// <summary>True when a search or category filter is narrowing the list.</summary>
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Query) || Category is not null;
}

/// <summary>Backing model for a single tool page, before and after execution.</summary>
/// <param name="Tool">The tool being shown.</param>
/// <param name="Result">The result of a server-side run, or null on first load.</param>
/// <param name="Errors">Field-level validation errors keyed by field name.</param>
/// <param name="Submitted">Values to re-populate the form with.</param>
public sealed record ToolPageViewModel(
    ToolDescriptor Tool,
    ToolResult? Result = null,
    IReadOnlyDictionary<string, string>? Errors = null,
    IReadOnlyDictionary<string, string>? Submitted = null)
{
    /// <summary>Validation errors, never null.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors => Errors ?? new Dictionary<string, string>();

    /// <summary>Submitted values, never null.</summary>
    public IReadOnlyDictionary<string, string> Values => Submitted ?? new Dictionary<string, string>();

    /// <summary>The value to render into a field, preferring what the user submitted.</summary>
    /// <param name="fieldName">The field's property name.</param>
    /// <param name="fallback">The field's default value.</param>
    public string ValueFor(string fieldName, string? fallback) =>
        Values.TryGetValue(fieldName, out string? submitted) ? submitted : fallback ?? string.Empty;
}

/// <summary>Backing model for the error page.</summary>
/// <param name="StatusCode">The HTTP status being reported.</param>
/// <param name="Title">Short heading.</param>
/// <param name="Message">One sentence explaining what happened.</param>
public sealed record ErrorViewModel(int StatusCode, string Title, string Message);
