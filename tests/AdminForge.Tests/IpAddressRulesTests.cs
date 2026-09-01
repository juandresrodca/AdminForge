using System.Net;
using AdminForge.Core.Net;

namespace AdminForge.Tests;

/// <summary>
/// The SSRF guard. Every server-side tool that takes a target depends on this being
/// right, so it gets the most explicit test coverage in the project.
/// </summary>
public sealed class IpAddressRulesTests
{
    [Theory]
    // Loopback and unspecified
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("::")]
    // RFC 1918
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    // Link-local, including cloud instance metadata
    [InlineData("169.254.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    // Carrier-grade NAT
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    // Documentation and benchmarking
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("2001:db8::1")]
    // Multicast and reserved
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("255.255.255.255")]
    [InlineData("ff02::1")]
    // Unique local
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    public void Blocks_private_and_reserved(string address) =>
        Assert.True(
            IpAddressRules.IsPrivateOrReserved(IPAddress.Parse(address)),
            $"{address} must be refused — it is not a legitimate public target.");

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("140.82.121.4")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001:80f::200e")]
    public void Allows_ordinary_public_addresses(string address) =>
        Assert.False(
            IpAddressRules.IsPrivateOrReserved(IPAddress.Parse(address)),
            $"{address} is public and should not be refused.");

    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:192.168.1.1")]
    public void An_ipv4_mapped_address_is_judged_on_its_ipv4_value(string address) =>
        Assert.True(
            IpAddressRules.IsPrivateOrReserved(IPAddress.Parse(address)),
            $"{address} wraps a private IPv4 address and must not be a way around the rules.");

    [Fact]
    public void An_ipv4_mapped_public_address_is_still_allowed() =>
        Assert.False(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("::ffff:8.8.8.8")));

    [Fact]
    public void The_boundaries_of_each_private_range_are_exact()
    {
        // One address either side of every RFC 1918 boundary.
        Assert.False(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("9.255.255.255")));
        Assert.True(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("10.0.0.0")));
        Assert.True(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("10.255.255.255")));
        Assert.False(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("11.0.0.0")));

        Assert.False(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("172.15.255.255")));
        Assert.True(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("172.16.0.0")));
        Assert.True(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("172.31.255.255")));
        Assert.False(IpAddressRules.IsPrivateOrReserved(IPAddress.Parse("172.32.0.0")));
    }

    [Fact]
    public void Throws_on_a_null_address() =>
        Assert.Throws<ArgumentNullException>(() => IpAddressRules.IsPrivateOrReserved(null!));
}
