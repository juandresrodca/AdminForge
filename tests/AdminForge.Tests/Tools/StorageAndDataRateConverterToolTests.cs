using System.Globalization;
using AdminForge.Core.Results;
using AdminForge.Tools.Ops.StorageAndDataRateConverter;

namespace AdminForge.Tests.Tools;

/// <summary>Covers storage conversions, data rates and transfer estimates.</summary>
public sealed class StorageAndDataRateConverterToolTests
{
    private static readonly StorageAndDataRateConverterTool Tool = new();

    [Fact]
    public async Task Compares_decimal_and_binary_terabytes()
    {
        ToolResult result = await RunAsync("4", "TB");

        Assert.True(result.Succeeded, result.Error);
        TableBlock table = Assert.Single(result.Blocks.OfType<TableBlock>());
        Assert.Equal(5, table.Rows.Count);
        IReadOnlyList<TableCell> row = table.Rows[3];
        Assert.Equal("4 TB", row[0].Value);
        Assert.Equal(3.64, double.Parse(row[1].Value.Split(' ')[0], CultureInfo.InvariantCulture), precision: 2);
        Assert.Contains("9.05", row[2].Value, StringComparison.Ordinal);
        Assert.Equal("4000000000000", Assert.Single(Assert.Single(result.Blocks.OfType<KeyValueBlock>()).Rows).Value);
    }

    [Theory]
    [InlineData("4", "TB", "TiB")]
    [InlineData("1.125", "PiB", "PB")]
    [InlineData("0.0004", "KiB", "KB")]
    public async Task Round_trips_storage_quantities(string value, string source, string target)
    {
        ToolResult forward = await RunAsync(value, source);
        string converted = QuantityIn(forward, target);
        ToolResult reverse = await RunAsync(converted, target);
        double expected = double.Parse(value, CultureInfo.InvariantCulture);
        double actual = double.Parse(QuantityIn(reverse, source), CultureInfo.InvariantCulture);

        Assert.InRange(Math.Abs(actual - expected) / expected, 0, 1e-13);
    }

    [Theory]
    [InlineData("100", "Mbps", "MB/s", "12.5")]
    [InlineData("1.25", "GB/s", "Gbps", "10")]
    [InlineData("1", "bps", "KB/s", "0.000125")]
    public async Task Converts_between_bits_and_bytes_per_second(string value, string source, string target, string expected)
    {
        ToolResult result = await RunAsync(value, source);

        Assert.True(result.Succeeded, result.Error);
        TableBlock table = Assert.Single(result.Blocks.OfType<TableBlock>());
        Assert.Equal(7, table.Rows.Count);
        Assert.Equal(expected, Assert.Single(table.Rows, row => row[0].Value == target)[1].Value);
    }

    [Fact]
    public async Task Estimates_time_and_size_at_the_same_rate()
    {
        ToolResult result = await RunAsync("100", "Mbps", size: "1", sizeUnit: "GB", seconds: "30");

        Assert.True(result.Succeeded, result.Error);
        KeyValueBlock estimates = Assert.Single(result.Blocks.OfType<KeyValueBlock>());
        Assert.Collection(estimates.Rows,
            row => Assert.Equal("80 s", row.Value),
            row => Assert.Equal("375000000 B", row.Value));
    }

    [Fact]
    public async Task Uses_binary_units_for_a_transfer_size()
    {
        ToolResult result = await RunAsync("1", "MB/s", size: "1", sizeUnit: "MiB");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("1.048576 s", Assert.Single(Assert.Single(result.Blocks.OfType<KeyValueBlock>()).Rows).Value);
    }

    [Theory]
    [InlineData("1e-60", "0.000")]
    [InlineData("1e60", "1000")]
    public async Task Formats_extreme_magnitudes_in_plain_decimal(string value, string prefix)
    {
        ToolResult result = await RunAsync(value, "B");

        Assert.True(result.Succeeded, result.Error);
        string bytes = Assert.Single(Assert.Single(result.Blocks.OfType<KeyValueBlock>()).Rows).Value;
        Assert.StartsWith(prefix, bytes, StringComparison.Ordinal);
        Assert.DoesNotContain('E', bytes);
        Assert.Equal(double.Parse(value, CultureInfo.InvariantCulture), double.Parse(bytes, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("", "TB", null, null, "Enter a non-negative number")]
    [InlineData("-1", "TB", null, null, "Enter a non-negative number")]
    [InlineData("NaN", "TB", null, null, "Enter a non-negative number")]
    [InlineData("1e-400", "TB", null, null, "Enter a non-negative number")]
    [InlineData("1e308", "KB", null, null, "The result is too large")]
    [InlineData("1", "mbps", null, null, "Choose a storage or data-rate unit")]
    [InlineData("1", "Mib", null, null, "Choose a storage or data-rate unit")]
    [InlineData("1", "TB", "2", null, "Choose a data-rate unit")]
    [InlineData("0", "Mbps", "1", null, "Enter a data rate greater than zero")]
    [InlineData("1", "Mbps", "-1", null, "Enter a non-negative transfer size")]
    [InlineData("1", "Mbps", null, "-1", "Enter a non-negative duration")]
    public async Task Describes_invalid_input(string value, string unit, string? size, string? seconds, string error)
    {
        ToolResult result = await RunAsync(value, unit, size, seconds: seconds);

        Assert.False(result.Succeeded);
        Assert.StartsWith(error, result.Error, StringComparison.Ordinal);
    }

    private static string QuantityIn(ToolResult result, string unit)
    {
        Assert.True(result.Succeeded, result.Error);
        TableBlock table = Assert.Single(result.Blocks.OfType<TableBlock>());
        string quantity = Assert.Single(table.Rows.SelectMany(row => row.Take(2)),
            cell => cell.Value.EndsWith(" " + unit, StringComparison.Ordinal)).Value;
        return quantity.Split(' ')[0];
    }

    private static Task<ToolResult> RunAsync(string value, string unit, string? size = null, string sizeUnit = "GB",
        string? seconds = null) =>
        Tool.ExecuteAsync(
            new StorageAndDataRateConverterInput
            {
                Value = value,
                Unit = unit,
                TransferSize = size,
                TransferSizeUnit = sizeUnit,
                TransferSeconds = seconds,
            },
            CancellationToken.None);
}
