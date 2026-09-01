namespace AdminForge.Core.Forms;

/// <summary>The input control rendered for a field.</summary>
public enum FieldKind
{
    /// <summary>Pick from the property type: string to text, bool to checkbox, numeric to number, enum to select.</summary>
    Auto,

    /// <summary>Single-line text box.</summary>
    Text,

    /// <summary>Multi-line text area. Use for pasted payloads: PEM, JSON, headers.</summary>
    TextArea,

    /// <summary>Numeric spinner.</summary>
    Number,

    /// <summary>Checkbox.</summary>
    Checkbox,

    /// <summary>Drop-down. Options come from the property's enum type or from <see cref="ToolFieldAttribute.Options"/>.</summary>
    Select,

    /// <summary>Masked text box. The value is never echoed back into the re-rendered form.</summary>
    Password,
}
