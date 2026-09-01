using AdminForge.Core.Results;

namespace AdminForge.Core.Tools;

/// <summary>
/// Implemented alongside <see cref="ITool"/> by tools whose <see cref="ITool.Compute"/>
/// is <see cref="ComputeMode.ServerSide"/>.
/// <para>
/// <typeparamref name="TInput"/> is a plain class whose properties carry
/// <see cref="Forms.ToolFieldAttribute"/>. The form UI is generated from that model,
/// bound from the posted request, validated, and handed to you fully populated —
/// so a handler is normally just the interesting logic and nothing else.
/// </para>
/// </summary>
/// <typeparam name="TInput">The tool's input model. Must be a class with a parameterless constructor.</typeparam>
public interface IToolHandler<in TInput> where TInput : class
{
    /// <summary>
    /// Run the tool.
    /// <para>
    /// Return <see cref="ToolResult.Fail(string)"/> for expected, user-facing problems
    /// ("that domain has no MX records", "not a valid CIDR"). Let genuinely unexpected
    /// exceptions propagate — the core catches them, logs them with the tool id, and
    /// shows a generic error rather than leaking internals to the page.
    /// </para>
    /// </summary>
    /// <param name="input">The bound, validated input model.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects or the tool timeout elapses.</param>
    Task<ToolResult> ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
