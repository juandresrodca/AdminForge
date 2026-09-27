using System.Globalization;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;
using DnsClient;
using DnsClient.Protocol;

namespace AdminForge.Tools.Email.MailAuth;

/// <summary>
/// Reads a domain's SPF, DKIM and DMARC records and says whether a receiver would
/// actually reject a forged message from it.
/// </summary>
/// <param name="validator">Vets a user-supplied resolver before it is queried.</param>
public sealed class MailAuthTool(IOutboundTargetValidator validator) : ITool, IToolHandler<MailAuthInput>
{
    /// <summary>
    /// Selectors worth probing before the user supplies their own. These cover the
    /// platforms most domains actually sign with.
    /// </summary>
    private static readonly string[] CommonSelectors =
        ["selector1", "selector2", "google", "default", "dkim", "k1", "k2", "mail", "smtp", "s1024", "zmail"];

    /// <inheritdoc />
    public string Id => "mail-auth-checker";

    /// <inheritdoc />
    public string Name => "SPF, DKIM and DMARC checker";

    /// <inheritdoc />
    public string Description => "Check whether a domain's mail authentication would actually stop a spoofed message";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Email;

    /// <inheritdoc />
    public string Icon => "mail";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["spf", "dkim", "dmarc", "spoofing", "deliverability", "mail flow", "exchange", "microsoft 365", "m365", "mx"];

    /// <inheritdoc />
    public string? Notes =>
        "DKIM cannot be enumerated from DNS — a selector is only discoverable from a signed message's "
        + "DKIM-Signature header. This tool probes the selectors that common platforms use, so a domain that signs "
        + "with a custom selector will show none found even though DKIM is working. Add yours in the second field.";

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(MailAuthInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        string domain = input.Domain.Trim().TrimEnd('.').ToLowerInvariant();

        if (domain.Length == 0 || !domain.Contains('.', StringComparison.Ordinal))
        {
            return ToolResult.Fail("Enter a domain, for example contoso.com.");
        }

        // This tool makes a dozen or more queries. A short per-query timeout keeps the
        // slowest selector from consuming the whole budget, and every lookup runs
        // concurrently so the total cost is one round trip rather than a dozen.
        LookupClientOptions clientOptions;

        if (string.IsNullOrWhiteSpace(input.Resolver))
        {
            clientOptions = new LookupClientOptions();
        }
        else
        {
            TargetValidation resolver = await validator
                .ValidateHostAsync(input.Resolver, cancellationToken)
                .ConfigureAwait(false);

            if (!resolver.IsAllowed)
            {
                return ToolResult.Fail(resolver.Reason!);
            }

            clientOptions = new LookupClientOptions(resolver.Addresses[0]);
        }

        clientOptions.Timeout = TimeSpan.FromSeconds(4);
        clientOptions.UseCache = false;
        clientOptions.ThrowDnsErrors = false;
        clientOptions.Retries = 1;

        // A large apex TXT answer overflows a UDP packet and has to be retried over TCP.
        // Without this, a busy domain silently looks like it has no SPF record at all.
        clientOptions.UseTcpFallback = true;

        var client = new LookupClient(clientOptions);

        Task<IReadOnlyList<string>> spfTask = SpfRecordsAsync(client, domain, cancellationToken);
        Task<IReadOnlyList<string>> dmarcTask = DmarcRecordsAsync(client, domain, cancellationToken);
        Task<IReadOnlyList<(string Selector, string Record)>> dkimTask =
            ProbeDkimAsync(client, domain, input.Selectors, cancellationToken);
        Task<IReadOnlyList<MxRecord>> mxTask = MxAsync(client, domain, cancellationToken);

        await Task.WhenAll(spfTask, dmarcTask, dkimTask, mxTask).ConfigureAwait(false);

        IReadOnlyList<string> spfRecords = await spfTask.ConfigureAwait(false);
        IReadOnlyList<string> dmarcRecords = await dmarcTask.ConfigureAwait(false);
        IReadOnlyList<(string Selector, string Record)> dkim = await dkimTask.ConfigureAwait(false);
        IReadOnlyList<MxRecord> mx = await mxTask.ConfigureAwait(false);

        string? spf = spfRecords.Count > 0 ? spfRecords[0] : null;
        string? dmarc = dmarcRecords.Count > 0 ? dmarcRecords[0] : null;

        // Includes are followed only when the record is usable at all: two SPF records
        // are a permerror before a single lookup happens.
        int spfLookups = spf is null || spfRecords.Count > 1
            ? 0
            : await SpfRecord.CountLookupsAsync(
                spf,
                async (name, token) =>
                {
                    IReadOnlyList<string> nested = await SpfRecordsAsync(client, name, token).ConfigureAwait(false);
                    return nested.Count == 1 ? nested[0] : null;
                },
                cancellationToken).ConfigureAwait(false);

        ResultBuilder result = ToolResult.Build();

        AppendVerdict(result, domain, spfRecords, dmarcRecords, dkim.Any(d => !IsRevokedDkim(d.Record)));
        AppendSpf(result, spfRecords, spfLookups);
        AppendDmarc(result, dmarcRecords);
        AppendDkim(result, dkim);
        AppendMx(result, mx);

        return result.ToResult();
    }

    private static void AppendVerdict(
        ResultBuilder result,
        string domain,
        IReadOnlyList<string> spfRecords,
        IReadOnlyList<string> dmarcRecords,
        bool anyDkim)
    {
        // More than one record of either kind is an error receivers treat as "none".
        string? spf = spfRecords.Count == 1 ? spfRecords[0] : null;
        string? dmarc = dmarcRecords.Count == 1 ? dmarcRecords[0] : null;
        string policy = ReadTag(dmarc, "p")?.ToLowerInvariant() ?? "none";
        bool enforcing = policy is "quarantine" or "reject";
        char? allQualifier = spf is null ? null : SpfRecord.AllQualifier(spf);
        bool spfStrict = allQualifier is '-' or '~';

        (ResultStatus status, string message, string detail) = (spf, dmarc) switch
        {
            (null, null) => (
                ResultStatus.Danger,
                "Anyone can spoof this domain",
                "Neither SPF nor DMARC is published, so receivers have nothing to check a forged message against."),

            (not null, null) => (
                ResultStatus.Warning,
                "SPF only — spoofing is still largely unblocked",
                "Without DMARC, receivers have no instruction about what to do when SPF fails, and the From "
                + "address a user sees is not covered at all."),

            (null, not null) => (
                ResultStatus.Warning,
                "DMARC without SPF",
                "DMARC has nothing to align against unless DKIM is signing every message."),

            _ when enforcing && spfStrict => (
                ResultStatus.Ok,
                $"Protected — DMARC policy is {policy}",
                "SPF ends in a fail or softfail qualifier and DMARC tells receivers to act on it."),

            _ when enforcing => (
                ResultStatus.Warning,
                $"DMARC is {policy}, but SPF does not close the door",
                allQualifier switch
                {
                    '+' => "SPF ends in +all (or a bare all), so it authorises every sender.",
                    '?' => "SPF ends in ?all, so unlisted senders get a neutral result rather than a fail.",
                    _ => "SPF has no all mechanism, so unlisted senders get a neutral result rather than a fail.",
                }),

            _ => (
                ResultStatus.Warning,
                "DMARC is in monitoring mode",
                "p=none collects reports but asks receivers to take no action. Move to quarantine, then reject."),
        };

        if (!anyDkim && enforcing)
        {
            detail += " No DKIM selector was found, so forwarded mail will likely fail alignment.";
        }

        if (spfRecords.Count > 1 || dmarcRecords.Count > 1)
        {
            (status, message) = (ResultStatus.Danger, "Duplicate records break mail authentication");
            detail = "More than one "
                + (spfRecords.Count > 1 ? "SPF" : "DMARC")
                + " record is published, which receivers treat as an error and ignore. Merge them into one.";
        }

        result.Status(status, message, $"{domain} — {detail}");
    }

    private static void AppendSpf(ResultBuilder result, IReadOnlyList<string> spfRecords, int lookups)
    {
        if (spfRecords.Count == 0)
        {
            result.Status(ResultStatus.Danger, "No SPF record", "Nothing at the domain apex starts with v=spf1.", "SPF");
            return;
        }

        if (spfRecords.Count > 1)
        {
            result.Status(
                ResultStatus.Danger,
                $"{spfRecords.Count} SPF records published",
                "RFC 7208 requires exactly one. With several, receivers return permerror and SPF fails for every "
                + "message. Merge them into a single record.",
                "SPF");
            result.List("SPF records", spfRecords);
            return;
        }

        string spf = spfRecords[0];
        string qualifier = SpfRecord.Describe(SpfRecord.AllQualifier(spf));

        result.KeyValues("SPF", kv =>
        {
            kv.Add("Record", spf, monospace: true);
            kv.Add("Final mechanism", qualifier, qualifier switch
            {
                "-all (fail)" => ResultStatus.Ok,
                "~all (softfail)" => ResultStatus.Ok,
                "?all (neutral)" => ResultStatus.Warning,
                "+all (pass anything)" => ResultStatus.Danger,
                _ => ResultStatus.Warning,
            });
            kv.Add(
                "DNS lookups used",
                lookups > SpfRecord.LookupLimit
                    ? $"more than {SpfRecord.LookupLimit}, including nested includes"
                    : $"{lookups} of {SpfRecord.LookupLimit}, including nested includes",
                lookups > SpfRecord.LookupLimit ? ResultStatus.Danger : lookups >= 8 ? ResultStatus.Warning : ResultStatus.Ok,
                monospace: true);
        });

        if (lookups > SpfRecord.LookupLimit)
        {
            result.Status(
                ResultStatus.Danger,
                "SPF exceeds the ten DNS lookup limit",
                "RFC 7208 requires receivers to return permerror once a record needs more than ten lookups, which "
                + "means SPF fails for every message. Flatten some includes.",
                "SPF limit");
        }
    }

    private static void AppendDmarc(ResultBuilder result, IReadOnlyList<string> dmarcRecords)
    {
        if (dmarcRecords.Count > 1)
        {
            result.Status(
                ResultStatus.Danger,
                $"{dmarcRecords.Count} DMARC records published",
                "RFC 7489 says a receiver that finds more than one record applies no DMARC policy at all. "
                + "Keep exactly one.",
                "DMARC");
            result.List("DMARC records", dmarcRecords);
            return;
        }

        string? dmarc = dmarcRecords.Count == 1 ? dmarcRecords[0] : null;

        if (dmarc is null)
        {
            result.Status(
                ResultStatus.Danger,
                "No DMARC record",
                "Publish a TXT record at _dmarc with v=DMARC1; p=none; rua=mailto:… to start collecting reports.",
                "DMARC");
            return;
        }

        string policy = ReadTag(dmarc, "p")?.ToLowerInvariant() ?? "none";
        string? subdomainPolicy = ReadTag(dmarc, "sp");
        string percent = ReadTag(dmarc, "pct") ?? "100";

        result.KeyValues("DMARC", kv =>
        {
            kv.Add("Record", dmarc, monospace: true);
            kv.Add("Policy (p)", policy, policy switch
            {
                "reject" => ResultStatus.Ok,
                "quarantine" => ResultStatus.Ok,
                _ => ResultStatus.Warning,
            });
            kv.AddIf(subdomainPolicy is not null, "Subdomain policy (sp)", subdomainPolicy);
            kv.Add("Applied to (pct)", percent + "%",
                percent == "100" ? ResultStatus.Neutral : ResultStatus.Warning, monospace: true);
            kv.Add("Aggregate reports (rua)", ReadTag(dmarc, "rua"), monospace: true);
            kv.Add("Forensic reports (ruf)", ReadTag(dmarc, "ruf"), monospace: true);
            kv.Add("DKIM alignment (adkim)", ReadTag(dmarc, "adkim") == "s" ? "strict" : "relaxed");
            kv.Add("SPF alignment (aspf)", ReadTag(dmarc, "aspf") == "s" ? "strict" : "relaxed");
        });
    }

    private static void AppendDkim(ResultBuilder result, IReadOnlyList<(string Selector, string Record)> dkim)
    {
        result.Table(
            $"DKIM selectors found ({dkim.Count})",
            ["Selector", "Key type", "Record"],
            dkim.Select(IReadOnlyList<TableCell> (d) =>
            [
                new TableCell(d.Selector, IsRevokedDkim(d.Record) ? ResultStatus.Warning : ResultStatus.Ok, Monospace: true),
                IsRevokedDkim(d.Record)
                    ? new TableCell("revoked (empty p=)", ResultStatus.Warning)
                    : new TableCell(ReadTag(d.Record, "k") ?? "rsa", Monospace: true),
                new TableCell(d.Record.Length > 110 ? d.Record[..110] + "…" : d.Record, Monospace: true),
            ]).ToList(),
            "None of the probed selectors resolved. That does not prove DKIM is off — add your selector above.");
    }

    private static void AppendMx(ResultBuilder result, IReadOnlyList<MxRecord> mx)
    {
        result.Table(
            $"MX records ({mx.Count})",
            ["Preference", "Mail exchanger"],
            mx.OrderBy(m => m.Preference)
                .Select(IReadOnlyList<TableCell> (m) =>
                [
                    new TableCell(m.Preference.ToString(CultureInfo.InvariantCulture), Monospace: true),
                    new TableCell(m.Exchange.Value.TrimEnd('.'), Monospace: true),
                ]).ToList(),
            "No MX records. This domain does not receive mail.");
    }

    private static async Task<IReadOnlyList<string>> SpfRecordsAsync(
        ILookupClient client,
        string domain,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> records = await TxtRecordsAsync(client, domain, cancellationToken).ConfigureAwait(false);
        return records.Where(SpfRecord.IsSpf).ToList();
    }

    private static async Task<IReadOnlyList<string>> DmarcRecordsAsync(
        ILookupClient client,
        string domain,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> records = await TxtRecordsAsync(client, "_dmarc." + domain, cancellationToken)
            .ConfigureAwait(false);

        // RFC 7489 section 6.6.3: a record must start with exactly v=DMARC1.
        return records
            .Where(r => r.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase)
                        && (r.Length == 8 || r[8] is ';' or ' '))
            .ToList();
    }

    /// <summary>True for a DKIM key record whose p= tag is present but empty, which RFC 6376 defines as revoked.</summary>
    private static bool IsRevokedDkim(string record) => ReadTag(record, "p")?.Length == 0;

    private static async Task<IReadOnlyList<(string, string)>> ProbeDkimAsync(
        ILookupClient client,
        string domain,
        string? extra,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> selectors = CommonSelectors;

        if (!string.IsNullOrWhiteSpace(extra))
        {
            selectors = selectors.Concat(
                extra.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        string[] wanted = selectors.Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToArray();

        (string Selector, string? Record)[] probed = await Task.WhenAll(
            wanted.Select(async selector =>
            {
                IReadOnlyList<string> records = await TxtRecordsAsync(
                    client,
                    $"{selector}._domainkey.{domain}",
                    cancellationToken).ConfigureAwait(false);

                // v= is optional, but p= is the one tag every key record must carry. Testing
                // for the tag rather than the text "p=" keeps a wildcard TXT answer from
                // passing for a key.
                string? key = records.FirstOrDefault(r => ReadTag(r, "p") is not null);

                return (selector, key);
            })).ConfigureAwait(false);

        return probed
            .Where(p => p.Record is not null)
            .Select(p => (p.Selector, p.Record!))
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> TxtRecordsAsync(
        ILookupClient client,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            IDnsQueryResponse response = await client
                .QueryAsync(name, QueryType.TXT, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Answers.OfType<TxtRecord>().Select(r => string.Concat(r.Text)).ToList();
        }
        catch (Exception ex) when (ex is DnsResponseException or System.Net.Sockets.SocketException)
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<MxRecord>> MxAsync(
        ILookupClient client,
        string domain,
        CancellationToken cancellationToken)
    {
        try
        {
            IDnsQueryResponse response = await client
                .QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Answers.OfType<MxRecord>().ToList();
        }
        catch (Exception ex) when (ex is DnsResponseException or System.Net.Sockets.SocketException)
        {
            return [];
        }
    }

    /// <summary>Read a <c>tag=value</c> pair out of an SPF or DMARC record.</summary>
    private static string? ReadTag(string? record, string tag)
    {
        if (string.IsNullOrWhiteSpace(record))
        {
            return null;
        }

        foreach (string part in record.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = part.IndexOf('=');

            if (equals > 0 && part[..equals].Trim().Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                return part[(equals + 1)..].Trim();
            }
        }

        return null;
    }
}
