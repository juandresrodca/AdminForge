using System.Reflection;
using System.Text.RegularExpressions;
using AdminForge.Core.Forms;
using AdminForge.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.Core.Registry;

/// <summary>
/// Finds <see cref="ITool"/> implementations by reflection and turns them into
/// validated <see cref="ToolDescriptor"/>s.
/// <para>
/// This is what makes "add a tool without touching the core" true: dropping a class
/// into the tools library is the entire registration step.
/// </para>
/// </summary>
public static partial class ToolDiscovery
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KebabCase { get; }

    /// <summary>Every concrete, instantiable tool type in the supplied assemblies.</summary>
    /// <param name="assemblies">Assemblies to scan.</param>
    public static IReadOnlyList<Type> FindToolTypes(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return assemblies
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                        && typeof(ITool).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Instantiate each tool through the container, validate it against the contract
    /// and snapshot its metadata.
    /// </summary>
    /// <param name="services">Provider used to resolve tool instances for metadata reading.</param>
    /// <param name="toolTypes">Types returned by <see cref="FindToolTypes"/>.</param>
    /// <exception cref="ToolRegistrationException">One or more tools break the contract.</exception>
    public static IReadOnlyList<ToolDescriptor> BuildDescriptors(
        IServiceProvider services,
        IReadOnlyList<Type> toolTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(toolTypes);

        var problems = new List<string>();
        var descriptors = new List<ToolDescriptor>(toolTypes.Count);
        var seenIds = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        // Tools may take scoped dependencies (HttpClient, options, loggers), so metadata
        // is read inside a throwaway scope. The instances are discarded immediately after.
        using IServiceScope scope = services.CreateScope();

        foreach (Type type in toolTypes)
        {
            ITool tool;

            try
            {
                tool = (ITool)ActivatorUtilities.CreateInstance(scope.ServiceProvider, type);
            }
            catch (Exception ex)
            {
                problems.Add($"{type.Name}: could not be constructed ({ex.GetBaseException().Message}). "
                             + "Every constructor dependency must be registered in DI.");
                continue;
            }

            int before = problems.Count;
            Validate(type, tool, seenIds, problems);

            if (problems.Count != before)
            {
                continue;
            }

            seenIds[tool.Id] = type;

            // A server tool takes its form shape from its handler; a client tool declares
            // the same shape through IClientTool so both render identical forms.
            Type? handlerInput = FindGenericArgument(type, typeof(IToolHandler<>));
            Type? formInput = handlerInput ?? FindGenericArgument(type, typeof(IClientTool<>));

            descriptors.Add(new ToolDescriptor(
                tool,
                type,
                ToolFormFactory.Create(formInput),
                handlerInput is null ? null : CreateInvoker(handlerInput)));
        }

        if (problems.Count > 0)
        {
            throw new ToolRegistrationException(problems);
        }

        return descriptors;
    }

    private static void Validate(Type type, ITool tool, Dictionary<string, Type> seenIds, List<string> problems)
    {
        string name = type.Name;

        if (string.IsNullOrWhiteSpace(tool.Id))
        {
            problems.Add($"{name}: Id is empty.");
        }
        else if (!KebabCase.IsMatch(tool.Id))
        {
            problems.Add($"{name}: Id '{tool.Id}' is not kebab-case. Use lowercase letters, "
                         + "digits and single hyphens, e.g. 'subnet-calculator'.");
        }
        else if (seenIds.TryGetValue(tool.Id, out Type? existing))
        {
            problems.Add($"{name}: Id '{tool.Id}' is already used by {existing.Name}. Ids must be unique.");
        }

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            problems.Add($"{name}: Name is empty.");
        }

        if (string.IsNullOrWhiteSpace(tool.Description))
        {
            problems.Add($"{name}: Description is empty. One sentence, sentence case, no trailing period.");
        }

        if (string.IsNullOrWhiteSpace(tool.Icon))
        {
            problems.Add($"{name}: Icon is empty. Use an id from wwwroot/icons/sprite.svg.");
        }

        Type? handlerInput = FindGenericArgument(type, typeof(IToolHandler<>));
        Type? clientInput = FindGenericArgument(type, typeof(IClientTool<>));

        switch (tool.Compute)
        {
            case ComputeMode.ServerSide when handlerInput is null:
                problems.Add($"{name}: Compute is ServerSide but the class does not implement "
                             + "IToolHandler<TInput>. Either implement it or mark the tool ClientSide.");
                break;

            case ComputeMode.ClientSide when handlerInput is not null:
                problems.Add($"{name}: Compute is ClientSide but the class implements IToolHandler<TInput>. "
                             + "A client-side tool never reaches the server, so the handler would be dead code.");
                break;

            case ComputeMode.ServerSide when clientInput is not null:
                problems.Add($"{name}: Compute is ServerSide but the class implements IClientTool<TInput>, "
                             + "which only describes a browser-rendered form. Drop it.");
                break;
        }

        Type? inputType = handlerInput ?? clientInput;

        if (inputType is not null && inputType.GetConstructor(Type.EmptyTypes) is null)
        {
            problems.Add($"{name}: input model {inputType.Name} needs a public parameterless constructor "
                         + "so the core can bind a posted form to it.");
        }

        if (CountHandlerInterfaces(type) > 1)
        {
            problems.Add($"{name}: declares more than one input model. A tool implements either "
                         + "IToolHandler<TInput> or IClientTool<TInput>, exactly once.");
        }
    }

    private static Type? FindGenericArgument(Type toolType, Type openInterface) =>
        toolType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface)
            ?.GetGenericArguments()[0];

    private static int CountHandlerInterfaces(Type toolType) =>
        toolType.GetInterfaces()
            .Count(i => i.IsGenericType
                        && (i.GetGenericTypeDefinition() == typeof(IToolHandler<>)
                            || i.GetGenericTypeDefinition() == typeof(IClientTool<>)));

    private static IHandlerInvoker CreateInvoker(Type inputType) =>
        (IHandlerInvoker)Activator.CreateInstance(typeof(HandlerInvoker<>).MakeGenericType(inputType))!;

    /// <summary>
    /// Types from an assembly, tolerating a partially loadable one. A single tool that
    /// fails to load should not hide every other tool in the same assembly.
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
