using System.Net;
using AdminForge.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;

namespace AdminForge.Tests.Web;

/// <summary>Only configured proxies may set the client address the rate limiter sees.</summary>
public sealed class TrustedProxiesTests
{
    [Fact]
    public void Keeps_loopback_trusted_when_nothing_is_configured()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxies.Apply(options, null);

        // ASP.NET Core's defaults, ::1 and 127.0.0.0/8, and nothing else.
        Assert.All(options.KnownProxies, a => Assert.True(IPAddress.IsLoopback(a)));
        Assert.All(options.KnownIPNetworks, n => Assert.True(IPAddress.IsLoopback(n.BaseAddress)));
        Assert.DoesNotContain(options.KnownIPNetworks, n => n.Contains(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public void Adds_addresses_and_ranges()
    {
        var options = new ForwardedHeadersOptions();

        TrustedProxies.Apply(options, ["10.0.0.5", " 172.16.0.0/12 ", ""]);

        Assert.Contains(IPAddress.Parse("10.0.0.5"), options.KnownProxies);
        Assert.Contains(options.KnownIPNetworks, n => n.Contains(IPAddress.Parse("172.20.1.1")));
    }

    [Theory]
    [InlineData("proxy.example.com")]
    [InlineData("10.0.0.0/99")]
    public void Refuses_an_entry_it_cannot_parse(string entry) =>
        Assert.Throws<InvalidOperationException>(() => TrustedProxies.Apply(new ForwardedHeadersOptions(), [entry]));
}
