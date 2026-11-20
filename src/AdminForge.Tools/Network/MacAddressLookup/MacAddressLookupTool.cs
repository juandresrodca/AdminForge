using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Network.MacAddressLookup;

/// <summary>
/// Identifies who made a network interface from its MAC address, and reads the two flag
/// bits that say whether there is a manufacturer to find at all. A DHCP lease table or an
/// ARP cache gives you a MAC and nothing else; the vendor is often the fastest way to
/// tell a printer from a hypervisor from a phone.
/// </summary>
public sealed class MacAddressLookupTool : ITool, IToolHandler<MacAddressLookupInput>
{
    private const string ExpectedFormat =
        "Enter a 48-bit MAC address such as 00:1A:2B:3C:4D:5E, 00-1A-2B-3C-4D-5E, 001A.2B3C.4D5E or 001A2B3C4D5E.";

    private readonly OuiRegistry _registry;

    /// <summary>Uses the registry that ships with AdminForge.</summary>
    public MacAddressLookupTool()
        : this(OuiRegistry.Shipped)
    {
    }

    /// <summary>Uses the given registry. For tests.</summary>
    /// <param name="registry">The registry to look addresses up in.</param>
    internal MacAddressLookupTool(OuiRegistry registry) => _registry = registry;

    /// <inheritdoc />
    public string Id => "mac-address-lookup";

    /// <inheritdoc />
    public string Name => "MAC address and OUI vendor lookup";

    /// <inheritdoc />
    public string Description => "Find the vendor behind a MAC address and whether it is local, random or multicast";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Network;

    /// <inheritdoc />
    public string Icon => "network";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["mac", "oui", "vendor", "manufacturer", "ieee", "arp", "dhcp", "ethernet", "eui-64", "hardware address"];

    /// <inheritdoc />
    public string? Notes =>
        "The vendor comes from a copy of the IEEE MA-L, MA-M and MA-S registries embedded in AdminForge, so the "
        + "lookup works offline and never tells anyone which devices are on your network. A registration names the "
        + "company that bought the block, which is often the chipset or network-card maker rather than the brand "
        + "on the box.";

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(MacAddressLookupInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!MacAddress.TryParse(input.Address, out MacAddress? parsed))
        {
            return Task.FromResult(ToolResult.Fail(
                string.IsNullOrWhiteSpace(input.Address)
                    ? ExpectedFormat
                    : $"'{input.Address.Trim()}' is not a MAC address. {ExpectedFormat}"));
        }

        MacAddress mac = parsed.Value;
        ResultBuilder result = ToolResult.Build();

        // A group address is built from the OUI of whoever defined it, so the lookup uses
        // the address with its I/G bit cleared.
        OuiAssignment? assignment = mac.IsLocallyAdministered ? null : _registry.Find(mac.WithoutGroupBit());

        AppendVerdict(result, mac, assignment);
        AppendRegistration(result, assignment);
        AppendBits(result, mac);

        result.KeyValues("Notations", kv => kv
            .Add("IEEE (hyphens)", mac.ToOctets('-'), monospace: true)
            .Add("Colons", mac.ToOctets(':'), monospace: true)
            .Add("Cisco", mac.ToCisco(), monospace: true)
            .Add("Bare", mac.ToBare(), monospace: true)
            .AddIf(!mac.IsMulticast, "IPv6 link-local (EUI-64)", mac.ToEui64LinkLocal(), monospace: true));

        return Task.FromResult(result.ToResult());
    }

    private void AppendVerdict(ResultBuilder result, MacAddress mac, OuiAssignment? assignment)
    {
        if (mac.IsBroadcast)
        {
            result.Status(
                ResultStatus.Info,
                "Broadcast address",
                "FF:FF:FF:FF:FF:FF reaches every station on the segment. No device owns it.");
            return;
        }

        if (WellKnownGroup(mac) is { } group)
        {
            result.Status(ResultStatus.Info, group.Message, group.Detail);
            return;
        }

        if (mac.IsLocallyAdministered)
        {
            result.Status(
                ResultStatus.Info,
                "Locally administered: there is no vendor to find",
                mac.IsMulticast
                    ? "The U/L bit is set, so this group address was defined locally rather than by a registered organisation."
                    : "The U/L bit is set, so the address was assigned by software rather than burned in by a manufacturer. "
                      + "That is usually a randomised Wi-Fi address (iOS, Android and Windows do this by default), "
                      + "a virtual machine, a container or a bridge.");
            return;
        }

        if (assignment is null)
        {
            result.Status(
                ResultStatus.Warning,
                "No registration found",
                $"{FormatPrefix(mac)} is not in the IEEE registries shipped with this instance{DataDate()}. "
                + "It may be newly assigned, or the address may be mistyped.");
            return;
        }

        // The IEEE registers some MA-L blocks to itself and sells them on as MA-M and MA-S
        // assignments. Landing on one of those means no smaller block covers this address.
        if (assignment.Organisation.Equals("IEEE Registration Authority", StringComparison.OrdinalIgnoreCase))
        {
            result.Status(
                ResultStatus.Warning,
                "Unassigned part of an IEEE block",
                $"{assignment.Display} is split into smaller MA-M and MA-S assignments, and none of those shipped with this "
                + $"instance{DataDate()} covers this address.");
            return;
        }

        if (assignment.Organisation.Equals("Private", StringComparison.OrdinalIgnoreCase))
        {
            result.Status(
                ResultStatus.Info,
                "Registered privately",
                $"{assignment.Display} is assigned, but the registrant paid the IEEE to keep its name off the public list.");
            return;
        }

        result.Status(
            ResultStatus.Ok,
            assignment.Organisation,
            mac.IsMulticast
                ? $"A multicast address defined by the owner of {assignment.Registry} block {assignment.Display}"
                : $"{assignment.Registry} block {assignment.Display}");
    }

    private void AppendRegistration(ResultBuilder result, OuiAssignment? assignment)
    {
        if (assignment is null)
        {
            return;
        }

        result.KeyValues("Registration", kv => kv
            .Add("Organisation", assignment.Organisation)
            .Add("Assignment", assignment.Display, monospace: true)
            .Add("Registry", assignment.Registry switch
            {
                "MA-L" => "MA-L: large block, 24-bit prefix, 16,777,216 addresses",
                "MA-M" => "MA-M: medium block, 28-bit prefix, 1,048,576 addresses",
                _ => "MA-S: small block, 36-bit prefix, 4,096 addresses",
            })
            .AddIf(_registry.Generated is not null, "Registry data from", _registry.Generated, monospace: true));
    }

    private static void AppendBits(ResultBuilder result, MacAddress mac) =>
        result.KeyValues("What the address says about itself", kv => kv
            .Add(
                "I/G bit (unicast or group)",
                mac.IsMulticast ? "1: group address (multicast or broadcast)" : "0: unicast, one interface",
                monospace: false)
            .Add(
                "U/L bit (universal or local)",
                mac.IsLocallyAdministered
                    ? "1: locally administered, assigned by software"
                    : "0: universally administered, assigned by a manufacturer",
                mac.IsLocallyAdministered ? ResultStatus.Warning : ResultStatus.Neutral)
            .Add("First octet", Convert.ToString(mac.FirstOctet, 2).PadLeft(8, '0'), monospace: true));

    private static (string Message, string Detail)? WellKnownGroup(MacAddress mac)
    {
        string bare = mac.ToBare();

        // RFC 1112: 01-00-5E followed by the low 23 bits of the IPv4 group.
        if (bare.StartsWith("01005E", StringComparison.Ordinal) && (mac.Value & 0x80_0000) == 0)
        {
            ulong low = mac.Value & 0x7F_FFFF;
            string lowest = $"224.{(low >> 16) & 0x7F}.{(low >> 8) & 0xFF}.{low & 0xFF}";
            return (
                "IPv4 multicast",
                $"RFC 1112 copies the low 23 bits of an IPv4 group into 01:00:5E, so this address stands for 32 groups, "
                + $"the lowest being {lowest}.");
        }

        // RFC 2464: 33-33 followed by the low 32 bits of the IPv6 group.
        if (bare.StartsWith("3333", StringComparison.Ordinal))
        {
            return (
                "IPv6 multicast",
                $"RFC 2464 maps an IPv6 group onto 33:33. The low 32 bits, {bare[4..8]}:{bare[8..]}, are the end of the group address.");
        }

        return null;
    }

    private string DataDate() => _registry.Generated is null ? string.Empty : $" (data from {_registry.Generated})";

    private static string FormatPrefix(MacAddress mac) => new OuiAssignment("MA-L", mac.Prefix(24), 24, string.Empty).Display;
}
