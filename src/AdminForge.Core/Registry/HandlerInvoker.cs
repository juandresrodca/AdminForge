using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Core.Registry;

/// <summary>
/// Non-generic bridge to a tool's generic <see cref="IToolHandler{TInput}"/>.
/// Built once per tool at startup, so dispatching a request costs one interface call
/// and two casts rather than any reflection.
/// </summary>
internal interface IHandlerInvoker
{
    Task<ToolResult> InvokeAsync(object toolInstance, object input, CancellationToken cancellationToken);
}

/// <summary>Closed-generic implementation created via <see cref="Type.MakeGenericType"/>.</summary>
/// <typeparam name="TInput">The handler's input model type.</typeparam>
internal sealed class HandlerInvoker<TInput> : IHandlerInvoker where TInput : class
{
    public Task<ToolResult> InvokeAsync(object toolInstance, object input, CancellationToken cancellationToken)
        => ((IToolHandler<TInput>)toolInstance).ExecuteAsync((TInput)input, cancellationToken);
}
