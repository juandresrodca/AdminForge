using AdminForge.Core.Results;
using AdminForge.Tools.Network.MacAddressLookup;

namespace AdminForge.Tests.Tools;

/// <summary>Behaviour of the MAC address and OUI vendor lookup.</summary>
public sealed class MacAddressLookupToolTests
{
    private static readonly MacAddressLookupTool Tool = new();

    private static Task<ToolResult> RunAsync(string address) =>
        Tool.ExecuteAsync(new MacAddressLookupInput { Address = address }, CancellationToken.None);

    private static StatusBlock Verdict(ToolResult result) => result.Blocks.OfType<StatusBlock>().First();

    private static string? Row(ToolResult result, string label) =>
        result.Blocks.OfType<KeyValueBlock>().SelectMany(b => b.Rows).FirstOrDefault(r => r.Label == label)?.Value;

    [Theory]
    [InlineData("00:50:56:12:34:56")]
    [InlineData("00-50-56-12-34-56")]
    [InlineData("0050.5612.3456")]
    [InlineData("005056123456")]
    [InlineData("  00:50:56:12:34:56  ")]
    [InlineData("00:50:56:ab:cd:ef")]
    [InlineData("0050.56ab.cdef")]
    public async Task Every_notation_finds_the_vendor_case_insensitively(string address)
    {
        ToolResult result = await RunAsync(address);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(ResultStatus.Ok, Verdict(result).Status);
        Assert.Equal("VMware, Inc.", Verdict(result).Message);
        Assert.Equal("00-50-56", Row(result, "Assignment"));
        Assert.StartsWith("MA-L", Row(result, "Registry"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:55:DA:01:23:45", "MA-M", "00-55-DA-0", "Shinko Technos co.,ltd.")]
    [InlineData("00:1B:C5:00:01:23", "MA-S", "00-1B-C5-00-0", "Converging Systems Inc.")]
    public async Task The_most_specific_registry_wins_over_the_ieee_parent_block(
        string address,
        string registry,
        string assignment,
        string organisation)
    {
        ToolResult result = await RunAsync(address);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(organisation, Verdict(result).Message);
        Assert.Equal(assignment, Row(result, "Assignment"));
        Assert.StartsWith(registry, Row(result, "Registry"), StringComparison.Ordinal);
    }

    [Theory]
    // 0x02 in the first octet: what a phone's randomised Wi-Fi address or a VM often looks like.
    [InlineData("02:42:AC:11:00:02")]
    [InlineData("DA:A1:19:00:11:22")]
    // Locally administered even though the rest of it looks like a VMware OUI.
    [InlineData("02:50:56:12:34:56")]
    public async Task A_locally_administered_address_says_so_instead_of_naming_a_vendor(string address)
    {
        ToolResult result = await RunAsync(address);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("Locally administered", Verdict(result).Message, StringComparison.Ordinal);
        Assert.Null(Row(result, "Organisation"));
        Assert.StartsWith("1:", Row(result, "U/L bit (universal or local)"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_multicast_address_is_reported_as_a_group_address()
    {
        ToolResult result = await RunAsync("01:00:0C:CC:CC:CC");

        Assert.True(result.Succeeded, result.Error);
        Assert.StartsWith("1:", Row(result, "I/G bit (unicast or group)"), StringComparison.Ordinal);

        // Cisco's CDP address: looked up with the group bit cleared, so the owner is found.
        Assert.Equal("Cisco Systems, Inc", Verdict(result).Message);
        Assert.Contains("multicast", Verdict(result).Detail, StringComparison.OrdinalIgnoreCase);

        // A group address never becomes an interface address, so no SLAAC address is derived.
        Assert.Null(Row(result, "IPv6 link-local (EUI-64)"));
    }

    [Theory]
    [InlineData("01:00:5E:7F:00:01", "IPv4 multicast", "224.127.0.1")]
    [InlineData("33:33:00:00:00:01", "IPv6 multicast", "0000:0001")]
    [InlineData("FF:FF:FF:FF:FF:FF", "Broadcast address", "every station")]
    public async Task Well_known_group_addresses_are_named(string address, string message, string detail)
    {
        ToolResult result = await RunAsync(address);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(message, Verdict(result).Message);
        Assert.Contains(detail, Verdict(result).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_every_notation_and_the_eui64_link_local_address()
    {
        ToolResult result = await RunAsync("001a2b3c4d5e");

        Assert.Equal("00-1A-2B-3C-4D-5E", Row(result, "IEEE (hyphens)"));
        Assert.Equal("00:1A:2B:3C:4D:5E", Row(result, "Colons"));
        Assert.Equal("001a.2b3c.4d5e", Row(result, "Cisco"));
        Assert.Equal("001A2B3C4D5E", Row(result, "Bare"));

        // RFC 4291 appendix A: FFFE in the middle and the U/L bit inverted (00 becomes 02).
        Assert.Equal("fe80::21a:2bff:fe3c:4d5e", Row(result, "IPv6 link-local (EUI-64)"));
    }

    [Fact]
    public async Task A_private_registration_is_not_reported_as_a_vendor_called_private()
    {
        ToolResult result = await RunAsync("00:01:01:00:00:01");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("Registered privately", Verdict(result).Message);
    }

    [Fact]
    public async Task An_unregistered_part_of_an_ieee_block_is_not_credited_to_the_ieee()
    {
        // 00-55-DA is the IEEE's own MA-L, carved into MA-M blocks 0 to E. Block F is
        // not in the shipped data, so the honest answer is that nothing covers it.
        OuiRegistry registry = new(
            new Dictionary<string, string> { ["0055DA"] = "IEEE Registration Authority" },
            new Dictionary<string, string> { ["0055DA0"] = "Shinko Technos co.,ltd." },
            new Dictionary<string, string>(),
            "2026-01-01");

        ToolResult result = await new MacAddressLookupTool(registry)
            .ExecuteAsync(new MacAddressLookupInput { Address = "00:55:DA:F0:00:01" }, CancellationToken.None);

        Assert.Equal("Unassigned part of an IEEE block", Verdict(result).Message);
        Assert.Contains("2026-01-01", Verdict(result).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_prefix_says_nothing_was_found()
    {
        OuiRegistry empty = new(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

        ToolResult result = await new MacAddressLookupTool(empty)
            .ExecuteAsync(new MacAddressLookupInput { Address = "00:50:56:12:34:56" }, CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("No registration found", Verdict(result).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("00:50:56:12:34")]
    [InlineData("00:50:56:12:34:56:78")]
    [InlineData("00:50:56-12:34:56")]
    [InlineData("0:50:56:12:34:56:")]
    [InlineData("00:50:56:12:34:GG")]
    [InlineData("0050.5612.34567")]
    [InlineData("00505612345")]
    [InlineData("+0050561234")]
    public async Task Junk_input_fails_and_names_the_expected_format(string address)
    {
        ToolResult result = await RunAsync(address);

        Assert.False(result.Succeeded);
        Assert.Contains("00:1A:2B:3C:4D:5E", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shipped_registry_holds_all_three_ieee_registries()
    {
        OuiRegistry registry = OuiRegistry.Shipped;

        Assert.True(registry.Count > 50_000, $"Only {registry.Count} assignments loaded.");
        Assert.NotNull(registry.Generated);
    }
}
