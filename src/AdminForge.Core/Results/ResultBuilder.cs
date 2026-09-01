namespace AdminForge.Core.Results;

/// <summary>
/// Fluent builder for a successful <see cref="ToolResult"/>.
/// <para>
/// Typical use inside a handler:
/// <code>
/// return ToolResult.Build()
///     .Status(ResultStatus.Ok, "Certificate is valid", $"Expires in {days} days")
///     .KeyValues("Subject", kv => kv
///         .Add("Common name", cert.Subject)
///         .Add("Issuer", cert.Issuer)
///         .Add("Serial", cert.SerialNumber, monospace: true))
///     .Table("Subject alternative names", ["Name", "Type"], sanRows)
///     .ToResult();
/// </code>
/// </para>
/// </summary>
public sealed class ResultBuilder
{
    private readonly List<ResultBlock> _blocks = [];

    /// <summary>Add a colour-coded verdict line.</summary>
    public ResultBuilder Status(ResultStatus status, string message, string? detail = null, string? title = null)
    {
        _blocks.Add(new StatusBlock { Status = status, Message = message, Detail = detail, Title = title });
        return this;
    }

    /// <summary>Add a definition list built through a nested builder.</summary>
    public ResultBuilder KeyValues(string? title, Action<KeyValueCollector> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var collector = new KeyValueCollector();
        build(collector);
        _blocks.Add(new KeyValueBlock { Title = title, Rows = collector.Rows });
        return this;
    }

    /// <summary>Add a definition list from rows you already have.</summary>
    public ResultBuilder KeyValues(string? title, IReadOnlyList<KeyValueRow> rows)
    {
        _blocks.Add(new KeyValueBlock { Title = title, Rows = rows });
        return this;
    }

    /// <summary>Add a table.</summary>
    public ResultBuilder Table(
        string? title,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<TableCell>> rows,
        string emptyMessage = "No results.")
    {
        _blocks.Add(new TableBlock
        {
            Title = title,
            Headers = headers,
            Rows = rows,
            EmptyMessage = emptyMessage,
        });
        return this;
    }

    /// <summary>Add a paragraph of prose.</summary>
    public ResultBuilder Text(string text, string? title = null)
    {
        _blocks.Add(new TextBlock { Text = text, Title = title });
        return this;
    }

    /// <summary>Add a copyable monospace payload block.</summary>
    public ResultBuilder Code(string content, string? language = null, string? title = null, bool copyable = true)
    {
        _blocks.Add(new CodeBlock { Content = content, Language = language, Title = title, Copyable = copyable });
        return this;
    }

    /// <summary>Add a bulleted (or numbered) list.</summary>
    public ResultBuilder List(string? title, IReadOnlyList<string> items, bool ordered = false)
    {
        _blocks.Add(new ListBlock { Title = title, Items = items, Ordered = ordered });
        return this;
    }

    /// <summary>Add a pre-built block. The escape hatch when the helpers do not fit.</summary>
    public ResultBuilder Add(ResultBlock block)
    {
        _blocks.Add(block);
        return this;
    }

    /// <summary>Finish building.</summary>
    public ToolResult ToResult() => new() { Succeeded = true, Blocks = _blocks };

    /// <summary>Allows a builder to be returned directly where a result is expected.</summary>
    public static implicit operator ToolResult(ResultBuilder builder) => builder.ToResult();

    /// <summary>Accumulates rows for a <see cref="KeyValueBlock"/>.</summary>
    public sealed class KeyValueCollector
    {
        internal List<KeyValueRow> Rows { get; } = [];

        /// <summary>Append a row.</summary>
        public KeyValueCollector Add(
            string label,
            string? value,
            ResultStatus status = ResultStatus.Neutral,
            bool monospace = false)
        {
            Rows.Add(new KeyValueRow(label, value ?? "—", status, monospace));
            return this;
        }

        /// <summary>Append a row only when <paramref name="condition"/> holds.</summary>
        public KeyValueCollector AddIf(
            bool condition,
            string label,
            string? value,
            ResultStatus status = ResultStatus.Neutral,
            bool monospace = false)
            => condition ? Add(label, value, status, monospace) : this;
    }
}
