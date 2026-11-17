using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;
using AdminForge.Tools.Network.SubnetCalculator;

namespace AdminForge.Tools.Network.IpRangeToCidr;

/// <summary>Converts inclusive IP ranges to CIDR blocks and expands blocks to their endpoints.</summary>
public sealed class IpRangeToCidrTool : ITool, IToolHandler<IpRangeToCidrInput>
{
    /// <inheritdoc />
    public string Id => "ip-range-to-cidr";

    /// <inheritdoc />
    public string Name => "IP range to CIDR converter";

    /// <inheritdoc />
    public string Description => "Convert an IPv4 or IPv6 range to the fewest CIDR blocks, or find the endpoints of each block";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Network;

    /// <inheritdoc />
    public string Icon => "network";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["cidr", "ip range", "ipv4", "ipv6", "subnet"];

    /// <inheritdoc />
    public string? Notes => "The conversion runs on your AdminForge instance and makes no outbound requests.";

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(IpRangeToCidrInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        string addresses = input.Addresses.Trim();
        int hyphen = addresses.IndexOf('-');
        ToolResult result = hyphen >= 0 && !addresses.Contains('/')
            ? ConvertRange(addresses[..hyphen].Trim(), addresses[(hyphen + 1)..].Trim())
            : ExpandBlocks(addresses);
        return Task.FromResult(result);
    }

    private static ToolResult ConvertRange(string start, string end)
    {
        if (!TryParseAddress(start, out IPAddress? first) || !TryParseAddress(end, out IPAddress? last))
        {
            return ToolResult.Fail("Enter two IP addresses, for example 192.0.2.10 - 192.0.2.87.");
        }

        if (first.AddressFamily != last.AddressFamily)
        {
            return ToolResult.Fail("Use two IPv4 addresses or two IPv6 addresses in the range.");
        }

        BigInteger startValue = SubnetMath.ToBigInteger(first);
        BigInteger endValue = SubnetMath.ToBigInteger(last);
        if (startValue > endValue)
        {
            return ToolResult.Fail("The start address must be less than or equal to the end address.");
        }

        int bits = AddressBits(first);
        var blocks = new List<string>();
        for (BigInteger current = startValue; current <= endValue;)
        {
            // A CIDR block can only start at a multiple of its size.
            int alignment = current.IsZero ? bits : (int)BigInteger.TrailingZeroCount(current);
            BigInteger remaining = endValue - current + 1;
            int hostBits = Math.Min(alignment, (int)remaining.GetBitLength() - 1);
            IPAddress network = SubnetMath.Add(first, current - startValue);
            blocks.Add($"{network}/{bits - hostBits}");
            current += BigInteger.One << hostBits;
        }

        return ToolResult.Build()
            .KeyValues("Range", kv => kv.Add("Total addresses", FormatCount(endValue - startValue + 1)))
            .Code(string.Join('\n', blocks), title: "CIDR blocks")
            .ToResult();
    }

    private static ToolResult ExpandBlocks(string addresses)
    {
        string[] blocks = addresses.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (blocks.Length == 0)
        {
            return ToolResult.Fail("Enter an IP range or at least one CIDR block, for example 192.0.2.0/24.");
        }

        var rows = new List<IReadOnlyList<TableCell>>(blocks.Length);
        foreach (string block in blocks)
        {
            int slash = block.IndexOf('/');
            if (slash < 0
                || !TryParseAddress(block[..slash], out IPAddress? address)
                || !int.TryParse(block.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
                || prefix > AddressBits(address))
            {
                return ToolResult.Fail($"'{block}' is not a valid CIDR block. Use an address and prefix, for example 192.0.2.0/24.");
            }

            IPAddress first = SubnetMath.NetworkAddress(address, prefix);
            IPAddress last = SubnetMath.LastAddress(first, prefix);
            BigInteger count = BigInteger.One << (AddressBits(address) - prefix);
            rows.Add(
            [
                new TableCell($"{first}/{prefix}", Monospace: true),
                new TableCell(first.ToString(), Monospace: true),
                new TableCell(last.ToString(), Monospace: true),
                new TableCell(FormatCount(count), Monospace: true),
            ]);
        }

        return ToolResult.Build()
            .Table("Address ranges", ["CIDR block", "First address", "Last address", "Total addresses"], rows)
            .ToResult();
    }

    private static bool TryParseAddress(string value, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        return value.IndexOfAny(['%', '[', ']']) < 0
            && IPAddress.TryParse(value, out address)
            && (address.AddressFamily == AddressFamily.InterNetworkV6 || address.ToString() == value);
    }

    private static int AddressBits(IPAddress address) => address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;

    private static string FormatCount(BigInteger count) => count.ToString("N0", CultureInfo.InvariantCulture);
}
