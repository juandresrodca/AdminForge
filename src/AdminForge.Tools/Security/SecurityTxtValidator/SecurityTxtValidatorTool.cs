using System.Globalization;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Security.SecurityTxtValidator;

/// <summary>
/// Fetches a domain's security.txt and checks the required RFC 9116 fields, expiry and
/// delivery location so vulnerability reports have a current route to the right team.
/// </summary>
/// <param name="fetcher">The vetted, size-capped HTTP client.</param>
public sealed class SecurityTxtValidatorTool(SafeHttpFetcher fetcher)
    : ITool, IToolHandler<SecurityTxtValidatorInput>
{
    private const string WellKnownPath = "/.well-known/security.txt";
    private const string LegacyPath = "/security.txt";

    private static readonly string[] FieldOrder =
    [
        "Contact",
        "Expires",
        "Encryption",
        "Acknowledgments",
        "Preferred-Languages",
        "Policy",
        "Hiring",
        "Canonical",
    ];

    /// <inheritdoc />
    public string Id => "security-txt-validator";

    /// <inheritdoc />
    public string Name => "security.txt validator";

    /// <inheritdoc />
    public string Description => "Check a domain's security.txt for RFC 9116 fields, expiry and HTTPS delivery";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Security;

    /// <inheritdoc />
    public string Icon => "shield";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["security.txt", "securitytxt", "rfc9116", "vulnerability disclosure", "contact", "expires", "pgp", "bug bounty"];

    /// <inheritdoc />
    public string? Notes =>
        "RFC 9116 requires security.txt at /.well-known/security.txt, with Contact and Expires fields. "
        + "The legacy /security.txt location is still checked so older deployments get a useful migration finding.";

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        SecurityTxtValidatorInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!TryBuildOrigin(input.Target, out Uri? origin, out string? inputError))
        {
            return ToolResult.Fail(inputError!);
        }

        Uri baseUri = origin!;

        var checks = new List<LocationCheck>(2);

        foreach ((string label, string path) in new[]
        {
            ("/.well-known/security.txt", WellKnownPath),
            ("/security.txt (legacy)", LegacyPath),
        })
        {
            Uri url = new(baseUri, path);
            (SafeHttpResponse? response, string? error) = await fetcher
                .FetchAsync(url.ToString(), HttpMethod.Get, cancellationToken)
                .ConfigureAwait(false);

            checks.Add(new LocationCheck(label, url, response, error));
        }

        LocationCheck? selected = checks.FirstOrDefault(c => IsSuccess(c.Response));

        if (selected is null)
        {
            string? transportError = checks
                .Select(c => c.Error)
                .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

            if (transportError is not null)
            {
                return ToolResult.Fail(transportError);
            }

            bool allMissing = checks.All(c => c.Response?.StatusCode is 404 or 410);

            if (!allMissing)
            {
                int status = checks
                    .Select(c => c.Response?.StatusCode)
                    .FirstOrDefault(s => s is not null && s.Value >= 400)
                    ?? 0;

                return ToolResult.Fail(
                    status == 0
                        ? "The domain did not return a security.txt response."
                        : $"The domain returned HTTP {status} for both security.txt locations.");
            }

            return ToolResult.Build()
                .Status(
                    ResultStatus.Danger,
                    "No security.txt found",
                    $"Checked {baseUri.Host}{WellKnownPath} and {baseUri.Host}{LegacyPath}.")
                .Text(
                    "Publish a current file at /.well-known/security.txt so security researchers know where to send "
                    + "vulnerability reports.",
                    "Next step")
                .ToResult();
        }

        ParsedSecurityTxt parsed = Parse(selected.Response!.Body);
        bool servedOverHttps = Uri.TryCreate(selected.Response.FinalUrl, UriKind.Absolute, out Uri? finalUri)
            && string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        string? contact = First(parsed.Fields, "Contact");
        string? expiresText = First(parsed.Fields, "Expires");
        bool hasExpires = DateTimeOffset.TryParse(
            expiresText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset expires);
        bool expired = hasExpires && expires <= DateTimeOffset.UtcNow;
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(contact))
        {
            missing.Add("Contact");
        }

        if (string.IsNullOrWhiteSpace(expiresText))
        {
            missing.Add("Expires");
        }

        var findings = new List<string>();

        if (missing.Count > 0)
        {
            findings.Add($"Missing mandatory field(s): {string.Join(", ", missing)}.");
        }

        if (!string.IsNullOrWhiteSpace(expiresText) && !hasExpires)
        {
            findings.Add("Expires is not a valid ISO 8601 date.");
        }
        else if (expired)
        {
            findings.Add($"Expires passed on {expires:yyyy-MM-dd HH:mm} UTC.");
        }

        if (!servedOverHttps)
        {
            findings.Add("The file was not served over HTTPS.");
        }

        if (selected.Label.Contains("legacy", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("The file is only at the legacy root location; publish it at /.well-known/security.txt.");
        }

        ResultStatus overallStatus = missing.Count > 0 || expired || (!string.IsNullOrWhiteSpace(expiresText) && !hasExpires)
            ? ResultStatus.Danger
            : servedOverHttps && !selected.Label.Contains("legacy", StringComparison.OrdinalIgnoreCase)
                ? ResultStatus.Ok
                : ResultStatus.Warning;

        string overallMessage = overallStatus switch
        {
            ResultStatus.Danger when expired => "security.txt has expired",
            ResultStatus.Danger => "security.txt is missing required fields",
            ResultStatus.Warning when !servedOverHttps => "security.txt is not served over HTTPS",
            ResultStatus.Warning => "security.txt uses the legacy location",
            _ => "security.txt is current and complete",
        };

        ResultBuilder result = ToolResult.Build()
            .Status(
                overallStatus,
                overallMessage,
                $"{selected.Label} · {selected.Response.FinalUrl}")
            .KeyValues("File", kv =>
            {
                kv.Add("Location", selected.Label, selected.Label.Contains("legacy", StringComparison.OrdinalIgnoreCase)
                    ? ResultStatus.Warning
                    : ResultStatus.Ok);
                kv.Add("URL", selected.Response.FinalUrl, monospace: true);
                kv.Add("HTTP status", selected.Response.StatusCode.ToString(CultureInfo.InvariantCulture),
                    selected.Response.StatusCode is >= 200 and < 300 ? ResultStatus.Ok : ResultStatus.Warning,
                    monospace: true);
                kv.Add("Served over HTTPS", servedOverHttps ? "yes" : "no",
                    servedOverHttps ? ResultStatus.Ok : ResultStatus.Danger);
                kv.Add("PGP signed", parsed.IsSigned ? "yes (signature block stripped before parsing)" : "no",
                    parsed.IsSigned ? ResultStatus.Info : ResultStatus.Neutral);
            })
            .KeyValues("RFC 9116 fields", kv =>
            {
                foreach (string field in FieldOrder)
                {
                    string? value = First(parsed.Fields, field);
                    ResultStatus fieldStatus = field switch
                    {
                        "Contact" when string.IsNullOrWhiteSpace(value) => ResultStatus.Danger,
                        "Expires" when string.IsNullOrWhiteSpace(value) => ResultStatus.Danger,
                        "Expires" when !hasExpires || expired => ResultStatus.Danger,
                        _ => ResultStatus.Neutral,
                    };

                    kv.Add(field, JoinValues(parsed.Fields, field), fieldStatus, monospace: field is "Contact" or "Expires");
                }
            });

        if (checks.Any(c => c != selected && IsSuccess(c.Response)))
        {
            result.Text(
                "A second copy was also found at the legacy location. Keep the RFC 9116 location authoritative and "
                + "remove stale duplicates.",
                "Duplicate location");
        }

        if (findings.Count > 0)
        {
            result.List("Findings", findings);
        }

        string[] unknownFields = parsed.Fields.Keys
            .Where(k => !FieldOrder.Contains(k, StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unknownFields.Length > 0)
        {
            result.List("Unrecognised fields", unknownFields);
        }

        if (selected.Response.BodyTruncated)
        {
            result.Status(
                ResultStatus.Warning,
                "The response was truncated",
                "Only the configured response limit was read; fields after that point may be missing.");
        }

        return result.ToResult();
    }

    private static bool TryBuildOrigin(string target, out Uri? origin, out string? error)
    {
        origin = null;
        error = null;
        string value = target.Trim();

        if (value.Length == 0)
        {
            error = "Enter a domain or URL first.";
            return false;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(parsed.Host)
            || parsed.UserInfo.Length > 0)
        {
            error = "Enter a valid HTTP(S) domain or URL without embedded credentials.";
            return false;
        }

        var builder = new UriBuilder(parsed)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        origin = builder.Uri;
        return true;
    }

    private static bool IsSuccess(SafeHttpResponse? response) =>
        response is not null && response.StatusCode is >= 200 and < 300;

    private static string? First(IReadOnlyDictionary<string, List<string>> fields, string name) =>
        fields.TryGetValue(name, out List<string>? values)
            ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
            : null;

    private static string JoinValues(IReadOnlyDictionary<string, List<string>> fields, string name) =>
        fields.TryGetValue(name, out List<string>? values) && values.Count > 0
            ? string.Join("; ", values)
            : "—";

    private static ParsedSecurityTxt Parse(string body)
    {
        string normalised = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        List<string> lines = normalised.Split('\n').ToList();
        bool signed = false;
        int signedMarker = lines.FindIndex(l => l.Trim() == "-----BEGIN PGP SIGNED MESSAGE-----");

        if (signedMarker >= 0)
        {
            signed = true;
            lines = lines[(signedMarker + 1)..];

            while (lines.Count > 0
                   && (lines[0].StartsWith("Hash:", StringComparison.OrdinalIgnoreCase)
                       || string.IsNullOrWhiteSpace(lines[0])))
            {
                lines.RemoveAt(0);
            }
        }

        int signatureMarker = lines.FindIndex(l => l.Trim() == "-----BEGIN PGP SIGNATURE-----");

        if (signatureMarker >= 0)
        {
            signed = true;
            lines = lines[..signatureMarker];
        }

        var fields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentName = null;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();

            if (signed && line.StartsWith("- ", StringComparison.Ordinal))
            {
                line = line[2..];
            }

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (char.IsWhiteSpace(line[0]) && currentName is not null)
            {
                List<string> existing = fields[currentName];

                if (existing.Count > 0 && line.Trim().Length > 0)
                {
                    existing[^1] += " " + line.Trim();
                }

                continue;
            }

            int colon = line.IndexOf(':');

            if (colon <= 0)
            {
                currentName = null;
                continue;
            }

            currentName = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            if (value.Length > 0)
            {
                if (!fields.TryGetValue(currentName, out List<string>? values))
                {
                    values = [];
                    fields[currentName] = values;
                }

                values.Add(value);
            }
        }

        return new ParsedSecurityTxt(fields, signed);
    }

    private sealed record LocationCheck(
        string Label,
        Uri RequestedUrl,
        SafeHttpResponse? Response,
        string? Error);

    private sealed record ParsedSecurityTxt(
        IReadOnlyDictionary<string, List<string>> Fields,
        bool IsSigned);
}
