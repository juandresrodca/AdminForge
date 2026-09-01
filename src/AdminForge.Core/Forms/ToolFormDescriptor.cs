namespace AdminForge.Core.Forms;

/// <summary>One selectable option in a <see cref="FieldKind.Select"/> control.</summary>
/// <param name="Value">The value posted back.</param>
/// <param name="Label">The text shown to the user.</param>
public readonly record struct FieldOption(string Value, string Label);

/// <summary>A single form field, resolved from a property and its <see cref="ToolFieldAttribute"/>.</summary>
public sealed record ToolFieldDescriptor
{
    /// <summary>The input model property name — also the posted form key.</summary>
    public required string Name { get; init; }

    /// <summary>The visible label.</summary>
    public required string Label { get; init; }

    /// <summary>The resolved control type. Never <see cref="FieldKind.Auto"/>.</summary>
    public required FieldKind Kind { get; init; }

    /// <summary>Placeholder text.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Help text shown under the control.</summary>
    public string? Help { get; init; }

    /// <summary>Whether an empty value is rejected.</summary>
    public bool Required { get; init; }

    /// <summary>Choices for a select. Empty for every other kind.</summary>
    public IReadOnlyList<FieldOption> Options { get; init; } = [];

    /// <summary>Maximum input length, or zero for unbounded.</summary>
    public int MaxLength { get; init; }

    /// <summary>Inclusive numeric lower bound, or NaN.</summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>Inclusive numeric upper bound, or NaN.</summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>Text area row count.</summary>
    public int Rows { get; init; } = 6;

    /// <summary>Whether the field renders at half width.</summary>
    public bool Half { get; init; }

    /// <summary>The default value taken from a freshly constructed input model.</summary>
    public string? DefaultValue { get; init; }
}

/// <summary>The complete generated form for one tool.</summary>
/// <param name="InputType">The input model type, or null for a tool with no server input model.</param>
/// <param name="Fields">The fields, already ordered for display.</param>
public sealed record ToolFormDescriptor(Type? InputType, IReadOnlyList<ToolFieldDescriptor> Fields)
{
    /// <summary>A form with no fields.</summary>
    public static readonly ToolFormDescriptor Empty = new(null, []);

    /// <summary>True when there is nothing to render.</summary>
    public bool IsEmpty => Fields.Count == 0;
}
