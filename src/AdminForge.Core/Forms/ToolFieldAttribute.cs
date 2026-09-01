namespace AdminForge.Core.Forms;

/// <summary>
/// Declares how one property of a tool's input model is presented in the UI.
/// <para>
/// This is the reason a tool needs no view file: the core reads these attributes and
/// renders a themed, accessible, correctly-labelled form. Contributors describe the
/// input; they do not write HTML.
/// </para>
/// </summary>
/// <param name="label">The visible field label.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ToolFieldAttribute(string label) : Attribute
{
    /// <summary>The visible field label.</summary>
    public string Label { get; } = label;

    /// <summary>Which control to render. Defaults to <see cref="FieldKind.Auto"/>.</summary>
    public FieldKind Kind { get; set; } = FieldKind.Auto;

    /// <summary>Greyed-out example text shown in an empty field. Use a realistic value.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Short explanation rendered under the field.</summary>
    public string? Help { get; set; }

    /// <summary>Reject an empty value before the handler runs.</summary>
    public bool Required { get; set; }

    /// <summary>
    /// Comma-separated choices for <see cref="FieldKind.Select"/> when the property is
    /// not an enum, e.g. <c>"A,AAAA,MX,TXT"</c>. Use <c>value|Label</c> pairs when the
    /// stored value and the display text differ.
    /// </summary>
    public string? Options { get; set; }

    /// <summary>Maximum accepted input length. Also rendered as the control's maxlength.</summary>
    public int MaxLength { get; set; }

    /// <summary>Inclusive lower bound for numeric fields.</summary>
    public double Min { get; set; } = double.NaN;

    /// <summary>Inclusive upper bound for numeric fields.</summary>
    public double Max { get; set; } = double.NaN;

    /// <summary>Rows for a <see cref="FieldKind.TextArea"/>.</summary>
    public int Rows { get; set; } = 6;

    /// <summary>
    /// Display order, ascending. Fields sharing an order fall back to declaration
    /// order, so leaving this alone is usually right.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Render at half width so two fields share a row on wide screens. Use for short,
    /// related inputs such as a host and a port.
    /// </summary>
    public bool Half { get; set; }
}
