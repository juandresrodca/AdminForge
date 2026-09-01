using System.Globalization;
using System.Net;
using System.Text.Json;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Network.RdapLookup;

/// <summary>
/// Looks up who registered a domain or owns an IP range, using RDAP — the structured,
/// JSON replacement for WHOIS that every registry now serves.
/// </summary>
/// <param name="fetcher">The vetted, size-capped HTTP client.</param>
public sealed class RdapLookupTool(SafeHttpFetcher fetcher) : ITool, IToolHandler<RdapLookupInput>
{
    /// <summary>
    /// The IANA-backed bootstrap service, which redirects to whichever registry is
    /// authoritative. Our fetcher re-validates every redirect it follows.
    /// </summary>
    private const string BootstrapBase = "https://rdap.org/";

    /// <inheritdoc />
    public string Id => "rdap-lookup";

    /// <inheritdoc />
    public string Name => "RDAP and WHOIS lookup";

    /// <inheritdoc />
    public string Description => "Find who registered a domain or owns an IP range, with registration and expiry dates";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Network;

    /// <inheritdoc />
    public string Icon => "identity";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["whois", "rdap", "registrar", "registrant", "domain expiry", "asn", "allocation", "abuse contact", "ripe", "arin"];

    /// <inheritdoc />
    public string? Notes =>
        "RDAP is the structured successor to WHOIS, and is what registries are now required to serve. Personal "
        + "registrant details are redacted at source under GDPR for most domains, so expect the registrar and the "
        + "dates rather than a name and address.";

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(RdapLookupInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        string query = input.Query.Trim().TrimEnd('.').ToLowerInvariant();

        if (query.Length == 0)
        {
            return ToolResult.Fail("Enter a domain name or an IP address.");
        }

        bool isIp = IPAddress.TryParse(query, out _);
        string url = BootstrapBase + (isIp ? "ip/" : "domain/") + Uri.EscapeDataString(query);

        (SafeHttpResponse? response, string? error) = await fetcher
            .FetchAsync(url, HttpMethod.Get, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            return ToolResult.Fail(error!);
        }

        if (response.StatusCode == 404)
        {
            return ToolResult.Fail(
                isIp
                    ? $"No registry holds an allocation record for {query}."
                    : $"No registration was found for {query}. Check the spelling, and note that RDAP covers "
                      + "registered domains rather than subdomains.");
        }

        if (response.StatusCode is 429 or 503)
        {
            return ToolResult.Fail("The registry is rate limiting RDAP queries right now. Try again shortly.");
        }

        if (response.StatusCode >= 400)
        {
            return ToolResult.Fail($"The registry returned HTTP {response.StatusCode} for {query}.");
        }

        JsonElement root;

        try
        {
            using JsonDocument document = JsonDocument.Parse(response.Body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ToolResult.Fail("The registry returned something that is not valid RDAP JSON.");
        }

        ResultBuilder result = ToolResult.Build();

        if (isIp)
        {
            AppendIpNetwork(result, root, query);
        }
        else
        {
            AppendDomain(result, root, query);
        }

        AppendEntities(result, root);

        if (input.ShowRaw)
        {
            result.Code(Prettify(response.Body), "json", "Raw RDAP response");
        }

        return result.ToResult();
    }

    private static void AppendDomain(ResultBuilder result, JsonElement root, string query)
    {
        DateTimeOffset? expiry = EventDate(root, "expiration");
        int? daysLeft = expiry is null ? null : (int)Math.Floor((expiry.Value - DateTimeOffset.UtcNow).TotalDays);

        (ResultStatus status, string message) = daysLeft switch
        {
            null => (ResultStatus.Info, $"Registration found for {query}"),
            < 0 => (ResultStatus.Danger, $"Expired {Math.Abs(daysLeft.Value):N0} days ago"),
            <= 30 => (ResultStatus.Warning, $"Expires in {daysLeft.Value:N0} days"),
            _ => (ResultStatus.Ok, $"Expires in {daysLeft.Value:N0} days"),
        };

        result.Status(status, message, Text(root, "ldhName") ?? query);

        result.KeyValues("Registration", kv =>
        {
            kv.Add("Domain", Text(root, "ldhName") ?? query, monospace: true);
            kv.Add("Handle", Text(root, "handle"), monospace: true);
            kv.Add("Registered", FormatDate(EventDate(root, "registration")), monospace: true);
            kv.Add("Last changed", FormatDate(EventDate(root, "last changed")), monospace: true);
            kv.Add("Expires", FormatDate(expiry), status == ResultStatus.Ok ? ResultStatus.Neutral : status, monospace: true);
            kv.Add("DNSSEC", DnssecState(root), DnssecState(root) == "signed" ? ResultStatus.Ok : ResultStatus.Neutral);
        });

        string[] statuses = StringArray(root, "status");

        if (statuses.Length > 0)
        {
            result.List("Registry status codes", statuses);
        }

        if (root.TryGetProperty("nameservers", out JsonElement nameservers)
            && nameservers.ValueKind == JsonValueKind.Array)
        {
            result.Table(
                $"Name servers ({nameservers.GetArrayLength()})",
                ["Host"],
                nameservers.EnumerateArray()
                    .Select(IReadOnlyList<TableCell> (ns) =>
                        [new TableCell((Text(ns, "ldhName") ?? "—").ToLowerInvariant(), Monospace: true)])
                    .ToList(),
                "The registry lists no name servers for this domain.");
        }
    }

    private static void AppendIpNetwork(ResultBuilder result, JsonElement root, string query)
    {
        result.Status(
            ResultStatus.Info,
            Text(root, "name") ?? $"Allocation covering {query}",
            $"{Text(root, "startAddress")} – {Text(root, "endAddress")}");

        result.KeyValues("Allocation", kv =>
        {
            kv.Add("Handle", Text(root, "handle"), monospace: true);
            kv.Add("Range", $"{Text(root, "startAddress")} – {Text(root, "endAddress")}", monospace: true);
            kv.Add("CIDR", CidrOf(root), monospace: true);
            kv.Add("Type", Text(root, "type"));
            kv.Add("Country", Text(root, "country"), monospace: true);
            kv.Add("Parent handle", Text(root, "parentHandle"), monospace: true);
            kv.Add("Registered", FormatDate(EventDate(root, "registration")), monospace: true);
            kv.Add("Last changed", FormatDate(EventDate(root, "last changed")), monospace: true);
        });

        string[] statuses = StringArray(root, "status");

        if (statuses.Length > 0)
        {
            result.List("Status", statuses);
        }
    }

    private static void AppendEntities(ResultBuilder result, JsonElement root)
    {
        if (!root.TryGetProperty("entities", out JsonElement entities) || entities.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var rows = new List<IReadOnlyList<TableCell>>();

        foreach (JsonElement entity in entities.EnumerateArray())
        {
            string roles = string.Join(", ", StringArray(entity, "roles"));
            string name = VCardValue(entity, "fn") ?? Text(entity, "handle") ?? "—";
            string? email = VCardValue(entity, "email");

            rows.Add(
            [
                new TableCell(roles.Length == 0 ? "—" : roles),
                new TableCell(name),
                new TableCell(email ?? "redacted", email is null ? ResultStatus.Neutral : ResultStatus.Info, Monospace: true),
            ]);
        }

        result.Table(
            "Contacts",
            ["Role", "Name", "Email"],
            rows,
            "The registry published no contact entities.");
    }

    private static string CidrOf(JsonElement root)
    {
        if (!root.TryGetProperty("cidr0_cidrs", out JsonElement cidrs) || cidrs.ValueKind != JsonValueKind.Array)
        {
            return "—";
        }

        var parts = new List<string>();

        foreach (JsonElement cidr in cidrs.EnumerateArray())
        {
            string? prefix = Text(cidr, "v4prefix") ?? Text(cidr, "v6prefix");
            string? length = cidr.TryGetProperty("length", out JsonElement len) && len.ValueKind == JsonValueKind.Number
                ? len.GetInt32().ToString(CultureInfo.InvariantCulture)
                : null;

            if (prefix is not null && length is not null)
            {
                parts.Add($"{prefix}/{length}");
            }
        }

        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    private static string DnssecState(JsonElement root) =>
        root.TryGetProperty("secureDNS", out JsonElement secure)
        && secure.TryGetProperty("delegationSigned", out JsonElement signed)
        && signed.ValueKind == JsonValueKind.True
            ? "signed"
            : "unsigned";

    private static DateTimeOffset? EventDate(JsonElement root, string action)
    {
        if (!root.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement item in events.EnumerateArray())
        {
            if (string.Equals(Text(item, "eventAction"), action, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParse(
                    Text(item, "eventDate"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    /// <summary>
    /// Pull one field out of a jCard. The format nests the value three levels deep in
    /// positional arrays: ["vcard", [["fn", {}, "text", "Example Registrar"], …]].
    /// </summary>
    private static string? VCardValue(JsonElement entity, string field)
    {
        if (!entity.TryGetProperty("vcardArray", out JsonElement vcard)
            || vcard.ValueKind != JsonValueKind.Array
            || vcard.GetArrayLength() < 2)
        {
            return null;
        }

        JsonElement properties = vcard[1];

        if (properties.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement property in properties.EnumerateArray())
        {
            if (property.ValueKind == JsonValueKind.Array
                && property.GetArrayLength() >= 4
                && property[0].ValueKind == JsonValueKind.String
                && string.Equals(property[0].GetString(), field, StringComparison.OrdinalIgnoreCase)
                && property[3].ValueKind == JsonValueKind.String)
            {
                return property[3].GetString();
            }
        }

        return null;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] StringArray(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!)
                .ToArray()
            : [];

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "—";

    private static string Prettify(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
