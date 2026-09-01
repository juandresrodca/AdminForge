using System.Globalization;
using System.Net;
using AdminForge.Core.Configuration;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Options;

namespace AdminForge.Tools.Network.DnsLookup;

/// <summary>Queries DNS for one record type, optionally against a resolver you choose.</summary>
/// <param name="validator">Vets the domain and any custom resolver before querying.</param>
/// <param name="options">Deployment options, read for the query timeout.</param>
public sealed class DnsLookupTool(IOutboundTargetValidator validator, IOptionsMonitor<AdminForgeOptions> options)
    : ITool, IToolHandler<DnsLookupInput>
{
    /// <inheritdoc />
    public string Id => "dns-lookup";

    /// <inheritdoc />
    public string Name => "DNS lookup";

    /// <inheritdoc />
    public string Description => "Query A, AAAA, MX, TXT, NS, CNAME, SOA, CAA, SRV and PTR records against any resolver";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Network;

    /// <inheritdoc />
    public string Icon => "globe";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["nslookup", "dig", "resolve", "nameserver", "mx", "txt", "spf", "caa", "srv", "reverse"];

    /// <inheritdoc />
    public string? Notes =>
        "Queries are sent from the AdminForge server, so the answer reflects what that machine sees — which is "
        + "the point when you are checking a split-horizon zone or a resolver that filters. Caching is disabled, "
        + "so every run is a fresh query.";

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(DnsLookupInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        string domain = input.Domain.Trim().TrimEnd('.');

        if (domain.Length == 0)
        {
            return ToolResult.Fail("Enter a domain to look up.");
        }

        LookupClient client;

        try
        {
            client = await BuildClientAsync(input.Resolver, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }

        QueryType queryType = MapQueryType(input.RecordType);
        IDnsQueryResponse response;

        try
        {
            // A PTR query needs the in-addr.arpa / ip6.arpa form, so an address typed
            // into the domain box is reversed for the caller rather than rejected.
            response = queryType == QueryType.PTR && IPAddress.TryParse(domain, out IPAddress? reverseTarget)
                ? await client.QueryReverseAsync(reverseTarget, cancellationToken).ConfigureAwait(false)
                : await client.QueryAsync(domain, queryType, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (DnsResponseException ex)
        {
            return ToolResult.Fail($"The resolver refused the query: {ex.DnsError}.");
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return ToolResult.Fail("The resolver did not answer in time. Check the resolver address and try again.");
        }

        if (response.HasError)
        {
            return ToolResult.Fail(
                $"The resolver returned {response.Header.ResponseCode} for {domain}. "
                + (response.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain
                    ? "That name does not exist."
                    : "Check the name and record type."));
        }

        var records = response.Answers.ToList();
        ResultBuilder result = ToolResult.Build();

        result.Status(
            records.Count > 0 ? ResultStatus.Ok : ResultStatus.Warning,
            records.Count > 0
                ? $"{records.Count} {input.RecordType} record{(records.Count == 1 ? "" : "s")} for {domain}"
                : $"No {input.RecordType} records for {domain}",
            $"Answered by {response.NameServer.Address} · {response.Header.ResponseCode}");

        result.Table(
            "Answers",
            ["Name", "TTL", "Type", "Value"],
            records.Select(RowFor).ToList(),
            $"The name resolves, but has no {input.RecordType} record.");

        if (response.Authorities.Count > 0 && records.Count == 0)
        {
            result.Table(
                "Authority section",
                ["Name", "TTL", "Type", "Value"],
                response.Authorities.Select(RowFor).ToList());
        }

        return result.ToResult();
    }

    private async Task<LookupClient> BuildClientAsync(string? resolver, CancellationToken cancellationToken)
    {
        int timeout = options.CurrentValue.ToolTimeoutSeconds;

        LookupClientOptions clientOptions;

        if (string.IsNullOrWhiteSpace(resolver))
        {
            clientOptions = new LookupClientOptions();
        }
        else
        {
            // A custom resolver is a user-supplied network target like any other, so it
            // goes through the same private-range rules as the tools that fetch URLs.
            TargetValidation validation = await validator
                .ValidateHostAsync(resolver, cancellationToken)
                .ConfigureAwait(false);

            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(validation.Reason);
            }

            IPAddress address = validation.Addresses[0];
            clientOptions = new LookupClientOptions(address);
        }

        clientOptions.Timeout = TimeSpan.FromSeconds(Math.Max(2, timeout - 2));
        clientOptions.UseCache = false;
        clientOptions.ThrowDnsErrors = false;
        clientOptions.Retries = 1;

        return new LookupClient(clientOptions);
    }

    private static IReadOnlyList<TableCell> RowFor(DnsResourceRecord record) =>
    [
        new TableCell(record.DomainName.Value.TrimEnd('.'), Monospace: true),
        new TableCell(record.TimeToLive.ToString(CultureInfo.InvariantCulture), Monospace: true),
        new TableCell(record.RecordType.ToString()),
        new TableCell(ValueOf(record), Monospace: true),
    ];

    private static string ValueOf(DnsResourceRecord record) => record switch
    {
        ARecord a => a.Address.ToString(),
        AaaaRecord aaaa => aaaa.Address.ToString(),
        MxRecord mx => $"{mx.Preference} {mx.Exchange.Value.TrimEnd('.')}",
        TxtRecord txt => string.Concat(txt.Text),
        NsRecord ns => ns.NSDName.Value.TrimEnd('.'),
        CNameRecord cname => cname.CanonicalName.Value.TrimEnd('.'),
        PtrRecord ptr => ptr.PtrDomainName.Value.TrimEnd('.'),
        SoaRecord soa =>
            $"{soa.MName.Value.TrimEnd('.')} {soa.RName.Value.TrimEnd('.')} "
            + $"serial={soa.Serial} refresh={soa.Refresh} retry={soa.Retry} expire={soa.Expire} minimum={soa.Minimum}",
        CaaRecord caa => $"{caa.Flags} {caa.Tag} \"{caa.Value}\"",
        SrvRecord srv => $"{srv.Priority} {srv.Weight} {srv.Port} {srv.Target.Value.TrimEnd('.')}",
        _ => record.ToString() ?? string.Empty,
    };

    private static QueryType MapQueryType(DnsRecordKind kind) => kind switch
    {
        DnsRecordKind.A => QueryType.A,
        DnsRecordKind.AAAA => QueryType.AAAA,
        DnsRecordKind.MX => QueryType.MX,
        DnsRecordKind.TXT => QueryType.TXT,
        DnsRecordKind.NS => QueryType.NS,
        DnsRecordKind.CNAME => QueryType.CNAME,
        DnsRecordKind.SOA => QueryType.SOA,
        DnsRecordKind.CAA => QueryType.CAA,
        DnsRecordKind.SRV => QueryType.SRV,
        DnsRecordKind.PTR => QueryType.PTR,
        _ => QueryType.A,
    };
}
