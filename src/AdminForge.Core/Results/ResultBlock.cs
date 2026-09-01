using System.Text.Json.Serialization;

namespace AdminForge.Core.Results;

/// <summary>
/// One renderable section of a tool's output.
/// <para>
/// Tools never emit HTML. They return blocks, and the core renders them — which is
/// why every tool in AdminForge looks consistent without its author touching CSS,
/// and why reviewing a new tool is about its logic rather than its markup.
/// </para>
/// <para>
/// The same shapes are produced by client-side tools in JavaScript and rendered by
/// the same set of components, so both compute modes are visually identical.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StatusBlock), "status")]
[JsonDerivedType(typeof(KeyValueBlock), "keyValue")]
[JsonDerivedType(typeof(TableBlock), "table")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(CodeBlock), "code")]
[JsonDerivedType(typeof(ListBlock), "list")]
public abstract record ResultBlock
{
    /// <summary>Optional heading rendered above the block.</summary>
    public string? Title { get; init; }
}

/// <summary>A single prominent verdict line — the headline answer, colour-coded.</summary>
public sealed record StatusBlock : ResultBlock
{
    /// <summary>Severity colouring.</summary>
    public required ResultStatus Status { get; init; }

    /// <summary>The verdict itself, e.g. "Certificate expires in 12 days".</summary>
    public required string Message { get; init; }

    /// <summary>Optional supporting line rendered smaller, beneath the message.</summary>
    public string? Detail { get; init; }
}

/// <summary>One label/value pair inside a <see cref="KeyValueBlock"/>.</summary>
/// <param name="Label">The field name.</param>
/// <param name="Value">The field value. Rendered as text — never as HTML.</param>
/// <param name="Status">Optional colouring for the value.</param>
/// <param name="Monospace">Render the value in the monospace face. Use for hashes, IPs and keys.</param>
public sealed record KeyValueRow(
    string Label,
    string Value,
    ResultStatus Status = ResultStatus.Neutral,
    bool Monospace = false);

/// <summary>A definition list. The workhorse block — most tools need only this one.</summary>
public sealed record KeyValueBlock : ResultBlock
{
    /// <summary>The rows, rendered in order.</summary>
    public required IReadOnlyList<KeyValueRow> Rows { get; init; }
}

/// <summary>One cell in a <see cref="TableBlock"/>.</summary>
/// <param name="Value">Cell text. Rendered as text — never as HTML.</param>
/// <param name="Status">Optional colouring.</param>
/// <param name="Monospace">Render in the monospace face.</param>
public sealed record TableCell(
    string Value,
    ResultStatus Status = ResultStatus.Neutral,
    bool Monospace = false)
{
    /// <summary>Convenience conversion so tools can build plain string rows.</summary>
    public static implicit operator TableCell(string value) => new(value);
}

/// <summary>A table. Rows must all have the same length as <see cref="Headers"/>.</summary>
public sealed record TableBlock : ResultBlock
{
    /// <summary>Column headings.</summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>Row data.</summary>
    public required IReadOnlyList<IReadOnlyList<TableCell>> Rows { get; init; }

    /// <summary>Message shown instead of the table when <see cref="Rows"/> is empty.</summary>
    public string EmptyMessage { get; init; } = "No results.";
}

/// <summary>A paragraph of prose. Use sparingly — prefer structured blocks.</summary>
public sealed record TextBlock : ResultBlock
{
    /// <summary>The text.</summary>
    public required string Text { get; init; }
}

/// <summary>A copyable monospace block for payloads: PEM, JSON, raw records, commands.</summary>
public sealed record CodeBlock : ResultBlock
{
    /// <summary>The content.</summary>
    public required string Content { get; init; }

    /// <summary>Informational language tag shown on the block, e.g. "json", "pem", "powershell".</summary>
    public string? Language { get; init; }

    /// <summary>Show a "copy" button. On by default — this is usually the point of a code block.</summary>
    public bool Copyable { get; init; } = true;
}

/// <summary>A bulleted list, for findings and recommendations.</summary>
public sealed record ListBlock : ResultBlock
{
    /// <summary>The items.</summary>
    public required IReadOnlyList<string> Items { get; init; }

    /// <summary>Render as a numbered list instead of bulleted.</summary>
    public bool Ordered { get; init; }
}
