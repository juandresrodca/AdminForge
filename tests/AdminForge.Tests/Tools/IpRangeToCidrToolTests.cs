using AdminForge.Core.Results;
using AdminForge.Tools.Network.IpRangeToCidr;

namespace AdminForge.Tests.Tools;

public sealed class IpRangeToCidrToolTests
{
    private static readonly IpRangeToCidrTool Tool = new();

    [Theory]
    [InlineData("192.0.2.0 - 192.0.2.255", "192.0.2.0/24", "256")]
    [InlineData("192.0.2.1-192.0.2.6", "192.0.2.1/32\n192.0.2.2/31\n192.0.2.4/31\n192.0.2.6/32", "6")]
    [InlineData("255.255.255.255 - 255.255.255.255", "255.255.255.255/32", "1")]
    [InlineData("0.0.0.0 - 255.255.255.255", "0.0.0.0/0", "4,294,967,296")]
    [InlineData("2001:db8::1 - 2001:db8::4", "2001:db8::1/128\n2001:db8::2/127\n2001:db8::4/128", "4")]
    [InlineData(":: - ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", "::/0", "340,282,366,920,938,463,463,374,607,431,768,211,456")]
    public async Task Finds_the_smallest_covering_set(string range, string blocks, string count)
    {
        ToolResult result = await RunAsync(range);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(blocks, Assert.Single(result.Blocks.OfType<CodeBlock>()).Content);
        Assert.Equal(count, Assert.Single(Assert.Single(result.Blocks.OfType<KeyValueBlock>()).Rows).Value);
    }

    [Fact]
    public async Task Expands_each_block_in_input_order()
    {
        ToolResult result = await RunAsync(" 192.0.2.9/24, 192.0.2.9/32\r\n2001:db8:1::/64 \t::ffff:192.0.2.8/127 ");

        Assert.True(result.Succeeded, result.Error);
        TableBlock table = Assert.Single(result.Blocks.OfType<TableBlock>());
        Assert.Collection(table.Rows,
            row => Assert.Equal(["192.0.2.0/24", "192.0.2.0", "192.0.2.255", "256"], row.Select(cell => cell.Value)),
            row => Assert.Equal(["192.0.2.9/32", "192.0.2.9", "192.0.2.9", "1"], row.Select(cell => cell.Value)),
            row => Assert.Equal(["2001:db8:1::/64", "2001:db8:1::", "2001:db8:1:0:ffff:ffff:ffff:ffff", "18,446,744,073,709,551,616"], row.Select(cell => cell.Value)),
            row => Assert.Equal(["::ffff:192.0.2.8/127", "::ffff:192.0.2.8", "::ffff:192.0.2.9", "2"], row.Select(cell => cell.Value)));
    }

    [Theory]
    [InlineData("", "Enter an IP range")]
    [InlineData("192.0.2.10 - 192.0.2.9", "The start address")]
    [InlineData("192.0.2.10 - 2001:db8::1", "Use two IPv4 addresses or two IPv6 addresses")]
    [InlineData("192.0.2 - 192.0.2.9", "Enter two IP addresses")]
    [InlineData("192.0.2.0/24, 192.0.2.0/33", "'192.0.2.0/33' is not a valid CIDR block")]
    [InlineData("2001:db8::/129", "'2001:db8::/129' is not a valid CIDR block")]
    [InlineData("fe80::1%4/64", "'fe80::1%4/64' is not a valid CIDR block")]
    [InlineData("[2001:db8::1]:443/64", "'[2001:db8::1]:443/64' is not a valid CIDR block")]
    public async Task Explains_invalid_input(string addresses, string error)
    {
        ToolResult result = await RunAsync(addresses);

        Assert.False(result.Succeeded);
        Assert.StartsWith(error, result.Error, StringComparison.Ordinal);
    }

    private static Task<ToolResult> RunAsync(string addresses) =>
        Tool.ExecuteAsync(new IpRangeToCidrInput { Addresses = addresses }, CancellationToken.None);
}
