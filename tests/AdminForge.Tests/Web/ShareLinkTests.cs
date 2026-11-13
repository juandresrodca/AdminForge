using AdminForge.Core.Registry;
using AdminForge.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace AdminForge.Tests.Web;

/// <summary>A shared link fills a server-side form in, and does nothing else.</summary>
[Collection(ToolRegistryCollection.Name)]
public sealed class ShareLinkTests(ToolRegistryFixture fixture)
{
    private readonly ToolInputBinder _binder = new();

    [Fact]
    public void Fills_the_fields_the_link_names()
    {
        IReadOnlyDictionary<string, string> values = Prefill(
            "dns-lookup",
            ("Domain", "  example.com "),
            ("RecordType", "mx"),
            ("NotAField", "ignored"));

        Assert.Equal("example.com", values["Domain"]);
        Assert.Equal("mx", values["RecordType"]);
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void Drops_a_value_the_form_would_refuse()
    {
        IReadOnlyDictionary<string, string> values = Prefill(
            "dns-lookup",
            ("Domain", new string('a', 5000)),
            ("RecordType", "NOPE"));

        Assert.Empty(values);
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("TRUE", "true")]
    [InlineData("anything-else", "false")]
    public void Reads_a_checkbox_as_true_or_false(string supplied, string expected)
    {
        IReadOnlyDictionary<string, string> values = Prefill("rdap-lookup", ("ShowRaw", supplied));

        Assert.Equal(expected, values["ShowRaw"]);
    }

    [Theory]
    [InlineData("jwt-decoder")]
    [InlineData("password-generator")]
    [InlineData("hash-generator")]
    [InlineData("text-encoder")]
    public void Never_fills_a_browser_side_tool(string toolId)
    {
        ToolDescriptor tool = fixture.Registry.Find(toolId)!;

        // Every query key a caller could guess is offered; none may come back.
        var query = new QueryCollection(tool.Form.Fields
            .Select(f => f.Name)
            .Append("Token")
            .Append("Input")
            .Distinct()
            .ToDictionary(name => name, _ => new StringValues("secret")));

        Assert.Empty(_binder.Prefill(tool, query));
    }

    [Theory]
    [InlineData("8080", "http://*:8080")]
    [InlineData("80", "http://*:80")]
    public void A_platform_port_becomes_the_listen_url(string port, string expected)
    {
        Assert.True(HostingPort.TryGetUrl(port, out string? url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("-1")]
    [InlineData("http")]
    public void Anything_that_is_not_a_port_is_ignored(string? port) =>
        Assert.False(HostingPort.TryGetUrl(port, out _));

    private IReadOnlyDictionary<string, string> Prefill(string toolId, params (string Key, string Value)[] pairs) =>
        _binder.Prefill(
            fixture.Registry.Find(toolId)!,
            new QueryCollection(pairs.ToDictionary(p => p.Key, p => new StringValues(p.Value))));
}
