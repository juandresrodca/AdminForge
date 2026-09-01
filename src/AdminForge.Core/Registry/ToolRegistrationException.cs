namespace AdminForge.Core.Registry;

/// <summary>
/// Thrown at startup when one or more tools break the registry contract.
/// <para>
/// AdminForge deliberately refuses to start rather than quietly dropping a malformed
/// tool: a contributor who mistypes an id gets a named, actionable error on their
/// first <c>dotnet run</c> instead of a tool that silently never appears.
/// </para>
/// </summary>
public sealed class ToolRegistrationException : Exception
{
    /// <summary>Creates the exception from the collected problems.</summary>
    /// <param name="problems">One message per contract violation.</param>
    public ToolRegistrationException(IReadOnlyList<string> problems)
        : base(BuildMessage(problems))
        => Problems = problems;

    /// <summary>Every problem found, not just the first.</summary>
    public IReadOnlyList<string> Problems { get; }

    private static string BuildMessage(IReadOnlyList<string> problems)
    {
        string heading = problems.Count == 1
            ? "AdminForge found a problem with a registered tool:"
            : $"AdminForge found {problems.Count} problems with the registered tools:";

        return heading
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            + Environment.NewLine
            + Environment.NewLine
            + "See CONTRIBUTING.md for the tool contract.";
    }
}
