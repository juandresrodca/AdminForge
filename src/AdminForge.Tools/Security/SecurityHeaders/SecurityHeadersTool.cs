using System.Globalization;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Security.SecurityHeaders;

/// <summary>One header AdminForge grades, and how to read its value.</summary>
/// <param name="Name">The header name.</param>
/// <param name="Why">What the header protects against, in one line.</param>
/// <param name="Weight">Points contributed to the grade when present and sane.</param>
/// <param name="Assess">Grades a present value, returning a status and a note.</param>
internal sealed record HeaderRule(
    string Name,
    string Why,
    int Weight,
    Func<string, (ResultStatus Status, string? Note)>? Assess = null);

/// <summary>
/// Fetches a URL and grades the response headers that actually change a browser's
/// security behaviour.
/// </summary>
/// <param name="fetcher">The vetted, size-capped HTTP client.</param>
public sealed class SecurityHeadersTool(SafeHttpFetcher fetcher) : ITool, IToolHandler<SecurityHeadersInput>
{
    /// <summary>
    /// Headers that carry real weight, with the reason each one matters. Leaky
    /// headers are handled separately because their presence is the problem.
    /// </summary>
    private static readonly HeaderRule[] Graded =
    [
        new("Content-Security-Policy", "Blocks injected script and other unexpected resource loads", 25, AssessCsp),
        new("Strict-Transport-Security", "Forces HTTPS for future visits, defeating downgrade attacks", 20, AssessHsts),
        new("X-Content-Type-Options", "Stops the browser guessing a response's content type", 15, AssessNosniff),
        new("X-Frame-Options", "Blocks clickjacking by refusing to be framed", 15, AssessFrameOptions),
        new("Referrer-Policy", "Limits how much of your URL leaks to other sites", 15),
        new("Permissions-Policy", "Switches off browser features the page does not need", 10),
    ];

    /// <summary>Headers that describe the stack and only help someone targeting it.</summary>
    private static readonly (string Name, string Why)[] Leaky =
    [
        ("Server", "Names the web server, and often its exact version"),
        ("X-Powered-By", "Names the application framework"),
        ("X-AspNet-Version", "Names the exact ASP.NET version"),
        ("X-AspNetMvc-Version", "Names the exact ASP.NET MVC version"),
        ("X-Generator", "Names the CMS or generator"),
        ("X-Drupal-Cache", "Confirms the CMS in use"),
    ];

    /// <inheritdoc />
    public string Id => "http-security-headers";

    /// <inheritdoc />
    public string Name => "HTTP security headers";

    /// <inheritdoc />
    public string Description => "Grade a site's response headers and see exactly which protections are missing";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Security;

    /// <inheritdoc />
    public string Icon => "shield";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["csp", "hsts", "headers", "hardening", "clickjacking", "nosniff", "referrer policy", "permissions policy"];

    /// <inheritdoc />
    public string? Notes =>
        "The grade weights headers by how much they actually change browser behaviour, so a strong CSP counts for "
        + "more than a Permissions-Policy. It is a starting point for a hardening conversation, not a compliance "
        + "verdict — a policy can be present and still be permissive.";

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(SecurityHeadersInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        (SafeHttpResponse? response, string? error) = await fetcher
            .FetchAsync(input.Url, HttpMethod.Get, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            return ToolResult.Fail(error!);
        }

        ResultBuilder result = ToolResult.Build();
        var rows = new List<IReadOnlyList<TableCell>>();
        int score = 0;
        int total = Graded.Sum(r => r.Weight);
        var missing = new List<string>();

        foreach (HeaderRule rule in Graded)
        {
            if (!response.Headers.TryGetValue(rule.Name, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                missing.Add($"{rule.Name} — {rule.Why.ToLowerInvariant()}");
                rows.Add(
                [
                    new TableCell(rule.Name, ResultStatus.Danger, Monospace: true),
                    new TableCell("Missing", ResultStatus.Danger),
                    new TableCell(rule.Why),
                ]);
                continue;
            }

            (ResultStatus status, string? note) = rule.Assess?.Invoke(value) ?? (ResultStatus.Ok, null);

            score += status switch
            {
                ResultStatus.Ok => rule.Weight,
                ResultStatus.Warning => rule.Weight / 2,
                _ => 0,
            };

            rows.Add(
            [
                new TableCell(rule.Name, status, Monospace: true),
                new TableCell(Truncate(value, 90), status, Monospace: true),
                new TableCell(note ?? rule.Why),
            ]);
        }

        int percent = (int)Math.Round(score * 100.0 / total, MidpointRounding.AwayFromZero);
        (string grade, ResultStatus gradeStatus) = Grade(percent);

        result.Status(
            gradeStatus,
            $"Grade {grade} — {percent}% of the weighted checks",
            $"HTTP {response.StatusCode} {response.ReasonPhrase} · {response.FinalUrl}");

        result.Table("Security headers", ["Header", "Value", "Assessment"], rows);

        var leaks = Leaky
            .Where(l => response.Headers.ContainsKey(l.Name))
            .Select(IReadOnlyList<TableCell> (l) =>
            [
                new TableCell(l.Name, ResultStatus.Warning, Monospace: true),
                new TableCell(Truncate(response.Headers[l.Name], 60), Monospace: true),
                new TableCell(l.Why),
            ])
            .ToList();

        result.Table(
            "Headers that reveal the stack",
            ["Header", "Value", "Why it matters"],
            leaks,
            "None found. Nothing in the response advertises the server or framework.");

        if (response.Hops.Count > 0)
        {
            result.Table(
                "Redirects followed",
                ["From", "Status", "To"],
                response.Hops.Select(IReadOnlyList<TableCell> (hop) =>
                [
                    new TableCell(hop.Url, Monospace: true),
                    new TableCell(hop.StatusCode.ToString(CultureInfo.InvariantCulture)),
                    new TableCell(hop.Location ?? "—", Monospace: true),
                ]).ToList());
        }

        if (missing.Count > 0)
        {
            result.List($"Add these next ({missing.Count})", missing);
        }

        if (input.ShowAllHeaders)
        {
            result.Table(
                $"All response headers ({response.Headers.Count})",
                ["Header", "Value"],
                response.Headers
                    .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(IReadOnlyList<TableCell> (h) =>
                    [
                        new TableCell(h.Key, Monospace: true),
                        new TableCell(Truncate(h.Value, 160), Monospace: true),
                    ]).ToList());
        }

        return result.ToResult();
    }

    private static (string Grade, ResultStatus Status) Grade(int percent) => percent switch
    {
        >= 90 => ("A", ResultStatus.Ok),
        >= 75 => ("B", ResultStatus.Ok),
        >= 55 => ("C", ResultStatus.Warning),
        >= 35 => ("D", ResultStatus.Warning),
        _ => ("F", ResultStatus.Danger),
    };

    private static (ResultStatus, string?) AssessCsp(string value)
    {
        string policy = value.ToLowerInvariant();

        if (policy.Contains("unsafe-inline", StringComparison.Ordinal)
            || policy.Contains("unsafe-eval", StringComparison.Ordinal))
        {
            return (ResultStatus.Warning, "Present, but allows unsafe-inline or unsafe-eval, which undoes much of the protection");
        }

        return policy.Contains("default-src", StringComparison.Ordinal)
            || policy.Contains("script-src", StringComparison.Ordinal)
            ? (ResultStatus.Ok, "Restricts where script may come from")
            : (ResultStatus.Warning, "Present, but sets neither default-src nor script-src");
    }

    private static (ResultStatus, string?) AssessHsts(string value)
    {
        string policy = value.ToLowerInvariant();
        int index = policy.IndexOf("max-age=", StringComparison.Ordinal);

        if (index < 0)
        {
            return (ResultStatus.Warning, "Present but carries no max-age, so browsers ignore it");
        }

        string digits = new(policy[(index + 8)..].TakeWhile(char.IsDigit).ToArray());

        if (!long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out long maxAge))
        {
            return (ResultStatus.Warning, "max-age is not a number");
        }

        // Six months is the floor the HSTS preload list requires.
        if (maxAge < 15768000)
        {
            return (ResultStatus.Warning, $"max-age of {maxAge:N0}s is under the six months preload expects");
        }

        return policy.Contains("includesubdomains", StringComparison.Ordinal)
            ? (ResultStatus.Ok, "Long max-age and covers subdomains")
            : (ResultStatus.Ok, "Long max-age, but does not cover subdomains");
    }

    private static (ResultStatus, string?) AssessNosniff(string value) =>
        value.Trim().Equals("nosniff", StringComparison.OrdinalIgnoreCase)
            ? (ResultStatus.Ok, "Content-type sniffing is off")
            : (ResultStatus.Warning, "The only meaningful value is nosniff");

    private static (ResultStatus, string?) AssessFrameOptions(string value)
    {
        string policy = value.Trim().ToUpperInvariant();

        return policy switch
        {
            "DENY" => (ResultStatus.Ok, "Framing refused outright"),
            "SAMEORIGIN" => (ResultStatus.Ok, "Framing allowed only from the same origin"),
            _ => (ResultStatus.Warning, $"'{value}' is not a value browsers honour — use DENY or SAMEORIGIN"),
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
