namespace AdminForge.Core.Results;

/// <summary>
/// What a tool hands back to the core. Either a failure with a message the user can
/// act on, or a list of blocks to render.
/// </summary>
public sealed record ToolResult
{
    /// <summary>False when the tool could not produce an answer.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// User-facing explanation when <see cref="Succeeded"/> is false. Write it for an
    /// admin who mistyped something, not for a developer reading a stack trace.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>The rendered output, in order.</summary>
    public IReadOnlyList<ResultBlock> Blocks { get; init; } = [];

    /// <summary>How long the tool took. Populated by the core; tools leave it alone.</summary>
    public TimeSpan? Elapsed { get; init; }

    /// <summary>An expected, user-facing failure.</summary>
    /// <param name="error">What went wrong and, ideally, what to try instead.</param>
    public static ToolResult Fail(string error) =>
        new() { Succeeded = false, Error = error };

    /// <summary>A success carrying the supplied blocks.</summary>
    /// <param name="blocks">The blocks to render.</param>
    public static ToolResult Success(params ResultBlock[] blocks) =>
        new() { Succeeded = true, Blocks = blocks };

    /// <summary>Start building a successful result fluently.</summary>
    public static ResultBuilder Build() => new();
}
