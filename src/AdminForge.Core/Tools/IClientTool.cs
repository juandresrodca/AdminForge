namespace AdminForge.Core.Tools;

/// <summary>
/// Declares the input model for a tool whose <see cref="ITool.Compute"/> is
/// <see cref="ComputeMode.ClientSide"/>.
/// <para>
/// It is the mirror image of <see cref="IToolHandler{TInput}"/>: it shapes the
/// generated form, but carries no execution method, because the work happens in the
/// browser module at <c>wwwroot/tools/{id}.js</c> and the input never reaches the
/// server at all.
/// </para>
/// <para>
/// Implementing it is optional. Omit it only when the browser module renders its own
/// controls — a file drop zone, for instance — rather than a field list.
/// </para>
/// </summary>
/// <typeparam name="TInput">The input model whose properties carry <see cref="Forms.ToolFieldAttribute"/>.</typeparam>
#pragma warning disable CA1040 // Marker interface: the type argument is the whole payload.
public interface IClientTool<TInput> where TInput : class;
#pragma warning restore CA1040
