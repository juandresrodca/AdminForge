using System.Net;
using AdminForge.Tools.Network.SubnetCalculator;

namespace AdminForge.Tests;

/// <summary>Address arithmetic, which has to be exactly right or the tool is worse than useless.</summary>
public sealed class SubnetMathTests
{
    [Theory]
    [InlineData("10.20.0.0/22", "10.20.0.0", "10.20.3.255", 22)]
    [InlineData("192.168.1.100/24", "192.168.1.0", "192.168.1.255", 24)]
    [InlineData("172.16.5.7/12", "172.16.0.0", "172.31.255.255", 12)]
    [InlineData("8.8.8.8/32", "8.8.8.8", "8.8.8.8", 32)]
    [InlineData("0.0.0.0/0", "0.0.0.0", "255.255.255.255", 0)]
    [InlineData("203.0.113.64/26", "203.0.113.64", "203.0.113.127", 26)]
    public void Parses_ipv4_cidr_and_derives_the_range(string input, string network, string last, int prefix)
    {
        Assert.True(SubnetMath.TryParse(input, out SubnetFacts? facts, out string? error), error);

        Assert.Equal(IPAddress.Parse(network), facts!.Network);
        Assert.Equal(IPAddress.Parse(last), facts.LastAddress);
        Assert.Equal(prefix, facts.Prefix);
        Assert.False(facts.IsIPv6);
    }

    [Theory]
    [InlineData("10.0.0.0 255.255.255.0", 24)]
    [InlineData("10.0.0.0 255.255.0.0", 16)]
    [InlineData("10.0.0.0/255.255.255.252", 30)]
    [InlineData("192.168.1.0 255.255.255.128", 25)]
    public void Accepts_a_dotted_decimal_mask(string input, int expectedPrefix)
    {
        Assert.True(SubnetMath.TryParse(input, out SubnetFacts? facts, out string? error), error);
        Assert.Equal(expectedPrefix, facts!.Prefix);
    }

    [Fact]
    public void A_bare_address_is_treated_as_a_single_host()
    {
        Assert.True(SubnetMath.TryParse("10.1.2.3", out SubnetFacts? facts, out _));

        Assert.Equal(32, facts!.Prefix);
        Assert.Equal(1, facts.TotalAddresses);
    }

    [Theory]
    [InlineData("2001:db8::/32", "2001:db8::", 32)]
    [InlineData("fe80::1/64", "fe80::", 64)]
    [InlineData("2606:4700:4700::1111/48", "2606:4700:4700::", 48)]
    public void Parses_ipv6(string input, string network, int prefix)
    {
        Assert.True(SubnetMath.TryParse(input, out SubnetFacts? facts, out string? error), error);

        Assert.Equal(IPAddress.Parse(network), facts!.Network);
        Assert.Equal(prefix, facts.Prefix);
        Assert.True(facts.IsIPv6);
        Assert.Null(facts.Mask);
        Assert.Null(facts.Wildcard);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("2001:db8::/129")]
    [InlineData("999.1.1.1/24")]
    [InlineData("")]
    public void Rejects_nonsense_with_a_message(string input)
    {
        Assert.False(SubnetMath.TryParse(input, out SubnetFacts? facts, out string? error));

        Assert.Null(facts);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Rejects_a_non_contiguous_mask()
    {
        // 255.0.255.0 has a one after a zero, which is not a valid subnet mask.
        Assert.False(SubnetMath.TryParse("10.0.0.0 255.0.255.0", out _, out string? error));
        Assert.Contains("mask", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(24, "255.255.255.0", "0.0.0.255")]
    [InlineData(30, "255.255.255.252", "0.0.0.3")]
    [InlineData(16, "255.255.0.0", "0.0.255.255")]
    [InlineData(32, "255.255.255.255", "0.0.0.0")]
    public void Builds_masks_and_wildcards(int prefix, string mask, string wildcard)
    {
        Assert.Equal(IPAddress.Parse(mask), SubnetMath.MaskFor(prefix, false));
        Assert.Equal(IPAddress.Parse(wildcard), SubnetMath.WildcardFor(prefix));
    }

    [Theory]
    [InlineData(24, 256)]
    [InlineData(30, 4)]
    [InlineData(0, 4294967296L)]
    [InlineData(31, 2)]
    public void Counts_total_addresses(int prefix, long expected)
    {
        Assert.True(SubnetMath.TryParse($"10.0.0.0/{prefix}", out SubnetFacts? facts, out _));
        Assert.Equal(expected, (long)facts!.TotalAddresses);
    }

    [Theory]
    [InlineData("10.0.0.0", 1, "10.0.0.1")]
    [InlineData("10.0.0.255", 1, "10.0.1.0")]
    [InlineData("10.0.1.0", -1, "10.0.0.255")]
    [InlineData("255.255.255.254", 1, "255.255.255.255")]
    public void Adds_an_offset_across_octet_boundaries(string start, int offset, string expected) =>
        Assert.Equal(IPAddress.Parse(expected), SubnetMath.Add(IPAddress.Parse(start), offset));

    [Fact]
    public void Adds_an_offset_across_ipv6()
    {
        IPAddress result = SubnetMath.Add(IPAddress.Parse("2001:db8::ffff"), 1);
        Assert.Equal(IPAddress.Parse("2001:db8::1:0"), result);
    }

    [Fact]
    public void Renders_ipv4_in_dotted_binary() =>
        Assert.Equal(
            "11000000.10101000.00000001.00000000",
            SubnetMath.ToBinary(IPAddress.Parse("192.168.1.0")));

    [Fact]
    public void Masking_an_ipv4_mapped_ipv6_address_uses_its_ipv4_value()
    {
        Assert.True(SubnetMath.TryParse("::ffff:10.0.0.5/24", out SubnetFacts? facts, out _));

        Assert.False(facts!.IsIPv6);
        Assert.Equal(IPAddress.Parse("10.0.0.0"), facts.Network);
    }
}
