using AdminForge.Tools.Email.MailAuth;

namespace AdminForge.Tests.Tools;

/// <summary>SPF records read term by term, as a receiver reads them.</summary>
public sealed class SpfRecordTests
{
    [Theory]
    [InlineData("v=spf1 -all", '-')]
    [InlineData("v=spf1 mx ~all", '~')]
    [InlineData("v=spf1 ?all", '?')]
    [InlineData("v=spf1 +all", '+')]
    // A bare all has the default qualifier, which is pass.
    [InlineData("v=spf1 mx all", '+')]
    [InlineData("V=SPF1 MX -ALL", '-')]
    public void Reads_the_all_qualifier(string record, char expected) =>
        Assert.Equal(expected, SpfRecord.AllQualifier(record));

    [Theory]
    [InlineData("v=spf1 mx")]
    // "-all" inside another term is not the all mechanism.
    [InlineData("v=spf1 include:spf-all.example.com")]
    [InlineData("v=spf1 redirect=_spf.example.com")]
    public void Finds_no_all_where_there_is_none(string record) =>
        Assert.Null(SpfRecord.AllQualifier(record));

    [Theory]
    [InlineData("v=spf1 -all", 0)]
    [InlineData("v=spf1 ip4:192.0.2.0/24 ip6:2001:db8::/32 -all", 0)]
    [InlineData("v=spf1 a mx ptr -all", 3)]
    // A CIDR length on a or mx still costs a lookup.
    [InlineData("v=spf1 a/24 mx/24 -all", 2)]
    [InlineData("v=spf1 a:mail.example.com mx:example.com/24 include:_spf.example.net exists:%{i}.example.com -all", 4)]
    // redirect counts only when there is no all, because otherwise it is ignored.
    [InlineData("v=spf1 redirect=_spf.example.com", 1)]
    [InlineData("v=spf1 redirect=_spf.example.com -all", 0)]
    // exp is a modifier that costs nothing during evaluation.
    [InlineData("v=spf1 exp=explain.example.com -all", 0)]
    public void Counts_the_lookups_a_record_costs_by_itself(string record, int expected) =>
        Assert.Equal(expected, SpfRecord.CountOwnLookups(record));

    [Fact]
    public void Follows_includes_and_redirects_but_not_macros()
    {
        IReadOnlyList<string> targets = SpfRecord.NestedTargets(
            "v=spf1 include:_spf.google.com include:%{d}.example.com redirect=_spf.example.com");

        Assert.Equal(["_spf.google.com", "_spf.example.com"], targets);
    }

    [Theory]
    [InlineData("v=spf1 -all", true)]
    [InlineData("v=spf1", true)]
    [InlineData("v=spf10 -all", false)]
    [InlineData("google-site-verification=abc", false)]
    public void Recognises_an_spf_record(string txt, bool expected) =>
        Assert.Equal(expected, SpfRecord.IsSpf(txt));

    [Fact]
    public async Task Counts_lookups_made_by_nested_includes()
    {
        var zone = new Dictionary<string, string>
        {
            ["a.example"] = "v=spf1 include:b.example mx -all",
            ["b.example"] = "v=spf1 a mx include:c.example ~all",
        };

        int total = await SpfRecord.CountLookupsAsync(
            "v=spf1 include:a.example include:missing.example -all",
            Resolver(zone),
            CancellationToken.None);

        // Top: 2 includes. a.example: include + mx. b.example: a, mx, include. c.example: none published.
        Assert.Equal(7, total);
    }

    [Fact]
    public async Task A_record_that_includes_itself_exceeds_the_limit_and_stops()
    {
        var zone = new Dictionary<string, string> { ["loop.example"] = "v=spf1 include:loop.example -all" };

        int total = await SpfRecord.CountLookupsAsync(
            "v=spf1 include:loop.example -all",
            Resolver(zone),
            CancellationToken.None);

        Assert.True(total > SpfRecord.LookupLimit);
    }

    private static Func<string, CancellationToken, Task<string?>> Resolver(Dictionary<string, string> zone) =>
        (name, _) => Task.FromResult(zone.TryGetValue(name, out string? record) ? record : null);
}
