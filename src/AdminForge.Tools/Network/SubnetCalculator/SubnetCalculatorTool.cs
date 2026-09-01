using System.Globalization;
using System.Net;
using System.Numerics;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Network.SubnetCalculator;

/// <summary>
/// Works out everything about an IPv4 or IPv6 network from a CIDR block, and
/// optionally splits it into equal subnets.
/// </summary>
public sealed class SubnetCalculatorTool : ITool, IToolHandler<SubnetCalculatorInput>
{
    /// <inheritdoc />
    public string Id => "subnet-calculator";

    /// <inheritdoc />
    public string Name => "Subnet calculator";

    /// <inheritdoc />
    public string Description => "Work out network, broadcast, usable range, mask and host count for any IPv4 or IPv6 block";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Network;

    /// <inheritdoc />
    public string Icon => "network";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["cidr", "netmask", "subnet mask", "wildcard", "vlsm", "supernet", "broadcast", "ipv4", "ipv6", "prefix"];

    /// <inheritdoc />
    public string? Notes =>
        "Pure arithmetic — the server does no network access for this tool, and nothing you enter leaves the "
        + "instance. A /31 is treated as an RFC 3021 point-to-point link and a /32 as a single host, so neither "
        + "reports a broadcast address.";

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(SubnetCalculatorInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!SubnetMath.TryParse(input.Network, out SubnetFacts? facts, out string? error))
        {
            return Task.FromResult(ToolResult.Fail(error!));
        }

        SubnetFacts net = facts!;
        ResultBuilder result = ToolResult.Build();

        result.Status(
            ResultStatus.Info,
            $"{net.Network}/{net.Prefix}",
            DescribeScope(net));

        result.KeyValues("Range", kv =>
        {
            kv.Add("Network address", net.Network.ToString(), monospace: true);
            kv.Add(net.IsIPv6 ? "Last address" : "Broadcast address", net.LastAddress.ToString(), monospace: true);

            (string first, string last, string count) = UsableRange(net);
            kv.Add("First usable", first, monospace: true);
            kv.Add("Last usable", last, monospace: true);
            kv.Add("Usable hosts", count, monospace: true);
            kv.Add("Total addresses", net.TotalAddresses.ToString("N0", CultureInfo.InvariantCulture), monospace: true);
        });

        result.KeyValues("Mask", kv =>
        {
            kv.Add("Prefix length", "/" + net.Prefix, monospace: true);
            kv.AddIf(net.Mask is not null, "Subnet mask", net.Mask?.ToString(), monospace: true);
            kv.AddIf(net.Wildcard is not null, "Wildcard mask", net.Wildcard?.ToString(), monospace: true);
            kv.AddIf(!net.IsIPv6, "Network in binary", net.IsIPv6 ? null : SubnetMath.ToBinary(net.Network), monospace: true);
            kv.AddIf(!net.IsIPv6, "Mask in binary", net.Mask is null ? null : SubnetMath.ToBinary(net.Mask), monospace: true);
        });

        if (!net.Address.Equals(net.Network))
        {
            result.KeyValues("Address you entered", kv =>
            {
                kv.Add("Host address", net.Address.ToString(), monospace: true);
                kv.Add("Belongs to", $"{net.Network}/{net.Prefix}", monospace: true);
            });
        }

        if (input.SplitInto is int newPrefix)
        {
            AppendSplit(result, net, newPrefix, input.SplitLimit);
        }

        return Task.FromResult(result.ToResult());
    }

    private static string DescribeScope(SubnetFacts net)
    {
        string scope = IpAddressRules.IsPrivateOrReserved(net.Network)
            ? "Private or reserved range"
            : "Publicly routable range";

        return net.IsIPv6
            ? $"IPv6 · {scope}"
            : $"IPv4 · {scope} · {ClassOf(net.Network)}";
    }

    /// <summary>
    /// The historical class letter. Classful addressing has been obsolete since CIDR,
    /// but the vocabulary survives in documentation and interviews, so it is reported.
    /// </summary>
    private static string ClassOf(IPAddress network)
    {
        byte first = network.GetAddressBytes()[0];

        return first switch
        {
            < 128 => "class A",
            < 192 => "class B",
            < 224 => "class C",
            < 240 => "class D (multicast)",
            _ => "class E (reserved)",
        };
    }

    private static (string First, string Last, string Count) UsableRange(SubnetFacts net)
    {
        int bits = net.Bits;

        if (net.Prefix == bits)
        {
            return (net.Network.ToString(), net.Network.ToString(), "1 (single host)");
        }

        if (!net.IsIPv6 && net.Prefix == bits - 1)
        {
            // RFC 3021: a /31 has two usable addresses on a point-to-point link.
            return (net.Network.ToString(), net.LastAddress.ToString(), "2 (RFC 3021 point-to-point)");
        }

        if (net.IsIPv6)
        {
            // IPv6 has no broadcast address, so every address in the prefix is assignable.
            return (
                net.Network.ToString(),
                net.LastAddress.ToString(),
                net.TotalAddresses.ToString("N0", CultureInfo.InvariantCulture));
        }

        BigInteger usable = net.TotalAddresses - 2;

        return (
            SubnetMath.Add(net.Network, 1).ToString(),
            SubnetMath.Add(net.LastAddress, -1).ToString(),
            usable.ToString("N0", CultureInfo.InvariantCulture));
    }

    private static void AppendSplit(ResultBuilder result, SubnetFacts net, int newPrefix, int limit)
    {
        if (newPrefix <= net.Prefix)
        {
            result.Status(
                ResultStatus.Warning,
                $"Cannot split /{net.Prefix} into /{newPrefix}",
                "The new prefix has to be longer than the one you started with.");
            return;
        }

        if (newPrefix > net.Bits)
        {
            result.Status(
                ResultStatus.Warning,
                $"/{newPrefix} is not valid for {(net.IsIPv6 ? "IPv6" : "IPv4")}",
                $"The longest prefix is /{net.Bits}.");
            return;
        }

        BigInteger count = BigInteger.One << (newPrefix - net.Prefix);
        BigInteger step = BigInteger.One << (net.Bits - newPrefix);
        int shown = (int)BigInteger.Min(count, Math.Clamp(limit, 1, 256));

        var rows = new List<IReadOnlyList<TableCell>>(shown);

        for (int i = 0; i < shown; i++)
        {
            IPAddress start = SubnetMath.Add(net.Network, step * i);
            IPAddress end = SubnetMath.LastAddress(start, newPrefix);

            rows.Add(
            [
                new TableCell($"{start}/{newPrefix}", Monospace: true),
                new TableCell(start.ToString(), Monospace: true),
                new TableCell(end.ToString(), Monospace: true),
            ]);
        }

        result.Table(
            $"Split into /{newPrefix} — {count:N0} subnets, showing {shown:N0}",
            ["Subnet", "First address", "Last address"],
            rows);
    }
}
