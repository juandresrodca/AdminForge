using System.Globalization;
using AdminForge.Core.Results;
using AdminForge.Tools.Ops.UptimeAndSlaPercentageCalculator;

namespace AdminForge.Tests.Tools;

/// <summary>Behaviour of the uptime and SLA calculator.</summary>
public sealed class UptimeAndSlaPercentageCalculatorToolTests
{
    private static readonly UptimeAndSlaPercentageCalculatorTool Tool = new();

    private static decimal Dec(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static Task<ToolResult> RunAsync(
        string? availability = null,
        string? allowed = null,
        string? actual = null,
        SlaWindow window = SlaWindow.Month,
        DowntimeUnit unit = DowntimeUnit.Minutes) =>
        Tool.ExecuteAsync(
            new UptimeAndSlaPercentageCalculatorInput
            {
                Availability = availability,
                AllowedDowntime = allowed,
                ActualDowntime = actual,
                Window = window,
                Unit = unit,
            },
            CancellationToken.None);

    [Theory]
    [InlineData("99.9", SlaWindow.Year, "8h 45m 57s")]
    [InlineData("99.9", SlaWindow.Month, "43m 49s")]
    [InlineData("99.99", SlaWindow.Month, "4m 22s")]
    [InlineData("99.9", SlaWindow.Day, "1m 26s")]
    [InlineData("99.99", SlaWindow.Day, "8.6s")]
    [InlineData("0", SlaWindow.Day, "1d")]
    [InlineData("100", SlaWindow.Year, "0s")]
    public void Converts_availability_to_allowed_downtime(string availability, SlaWindow window, string expected) =>
        Assert.Equal(expected, SlaMath.FormatDuration(SlaMath.AllowedDowntime(Dec(availability), window)));

    [Theory]
    [InlineData("31557.6", SlaWindow.Year, "99.9")]
    [InlineData("262.98", SlaWindow.Month, "99.99")]
    [InlineData("0", SlaWindow.Week, "100")]
    [InlineData("86400", SlaWindow.Day, "0")]
    public void Converts_downtime_back_to_availability(string seconds, SlaWindow window, string expected) =>
        Assert.Equal(Dec(expected), SlaMath.Availability(Dec(seconds), window));

    [Theory]
    [InlineData("99.9")]
    [InlineData("99.95")]
    [InlineData("99.999")]
    [InlineData("42.123")]
    public void Round_trips_in_every_window(string availability)
    {
        foreach (SlaWindow window in Enum.GetValues<SlaWindow>())
        {
            decimal downtime = SlaMath.AllowedDowntime(Dec(availability), window);

            Assert.Equal(Dec(availability), SlaMath.Availability(downtime, window));
        }
    }

    [Theory]
    [InlineData("80", "fewer than one nine")]
    [InlineData("90", "one nine")]
    [InlineData("99", "two nines")]
    [InlineData("99.5", "two and a half nines")]
    [InlineData("99.9", "three nines")]
    [InlineData("99.95", "three and a half nines")]
    [InlineData("99.97", "between three and four nines")]
    [InlineData("99.999", "five nines")]
    public void Names_the_nines(string availability, string expected) =>
        Assert.Equal(expected, SlaMath.NinesName(Dec(availability)));

    [Fact]
    public async Task Shows_the_downtime_for_every_window()
    {
        ToolResult result = await RunAsync(availability: "99.9%");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(5, result.Blocks.OfType<TableBlock>().First(b => b.Title == "Allowed downtime").Rows.Count);
    }

    [Fact]
    public async Task Works_backwards_from_an_allowed_downtime()
    {
        ToolResult result = await RunAsync(allowed: "43.83");

        Assert.True(result.Succeeded, result.Error);
        Assert.StartsWith("99.9%", result.Blocks.OfType<StatusBlock>().First().Message, StringComparison.Ordinal);

        // The same budget, expressed per year, must match the forward direction exactly.
        TableBlock table = result.Blocks.OfType<TableBlock>().First(b => b.Title == "Allowed downtime");
        Assert.Equal("8h 45m 57s", table.Rows[^1][1].Value);
    }

    [Theory]
    [InlineData("99.9", "30", ResultStatus.Ok)]
    [InlineData("99.9", "60", ResultStatus.Danger)]
    [InlineData("100", "0", ResultStatus.Ok)]
    [InlineData("100", "1", ResultStatus.Danger)]
    public async Task Checks_actual_downtime_against_the_target(string availability, string actual, ResultStatus expected)
    {
        ToolResult result = await RunAsync(availability, actual: actual);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Title == "Actual downtime" && b.Status == expected);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("99.9", "10", null)]
    [InlineData("101", null, null)]
    [InlineData("-1", null, null)]
    [InlineData("abc", null, null)]
    [InlineData(null, "-5", null)]
    [InlineData(null, "1,5", null)]
    [InlineData("99.9", null, "50000")]
    public async Task Explains_itself_when_the_input_is_unusable(string? availability, string? allowed, string? actual)
    {
        ToolResult result = await RunAsync(availability, allowed, actual);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
