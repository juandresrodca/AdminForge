using AdminForge.Core.Results;
using AdminForge.Tools.Ops.CronParser;

namespace AdminForge.Tests;

/// <summary>Cron parsing, macro expansion and time-zone handling.</summary>
public sealed class CronParserTests
{
    private static readonly CronParserTool Tool = new();

    private static async Task<ToolResult> ParseAsync(string expression, string zone = "UTC", int occurrences = 5) =>
        await Tool.ExecuteAsync(
            new CronParserInput { Expression = expression, TimeZone = zone, Occurrences = occurrences },
            CancellationToken.None);

    [Theory]
    [InlineData("30 2 * * 1-5")]
    [InlineData("0 * * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0 1 * *")]
    [InlineData("0 0 1 1 *")]
    [InlineData("0 30 2 * * *")]
    public async Task Parses_valid_expressions(string expression)
    {
        ToolResult result = await ParseAsync(expression);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Status == ResultStatus.Ok);
    }

    [Theory]
    [InlineData("@hourly")]
    [InlineData("@daily")]
    [InlineData("@weekly")]
    [InlineData("@monthly")]
    [InlineData("@yearly")]
    [InlineData("@midnight")]
    public async Task Expands_macros_and_shows_the_equivalent(string macro)
    {
        ToolResult result = await ParseAsync(macro);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks.OfType<KeyValueBlock>(), b => b.Title == "Macro");
    }

    [Theory]
    [InlineData("", "expression")]
    [InlineData("* * *", "five fields")]
    [InlineData("* * * * * * *", "five fields")]
    [InlineData("99 * * * *", "valid cron")]
    [InlineData("not a cron", "five fields")]
    [InlineData("nope * * * *", "valid cron")]
    public async Task Rejects_bad_input_with_an_explanation(string expression, string expectedFragment)
    {
        ToolResult result = await ParseAsync(expression);

        Assert.False(result.Succeeded);
        Assert.Contains(expectedFragment, result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lists_the_requested_number_of_occurrences()
    {
        ToolResult result = await ParseAsync("0 * * * *", occurrences: 7);

        TableBlock runs = result.Blocks.OfType<TableBlock>().Single(b => b.Title!.StartsWith("Next", StringComparison.Ordinal));
        Assert.Equal(7, runs.Rows.Count);
    }

    [Fact]
    public async Task Breaks_the_expression_into_named_fields()
    {
        ToolResult result = await ParseAsync("30 2 * * 1-5");

        TableBlock fields = result.Blocks.OfType<TableBlock>().Single(b => b.Title == "Fields");

        Assert.Equal(5, fields.Rows.Count);
        Assert.Equal("Minute", fields.Rows[0][0].Value);
        Assert.Equal("Day of week", fields.Rows[4][0].Value);
        Assert.Contains("through", fields.Rows[4][2].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_six_field_expression_is_labelled_with_seconds()
    {
        ToolResult result = await ParseAsync("15 30 2 * * *");

        TableBlock fields = result.Blocks.OfType<TableBlock>().Single(b => b.Title == "Fields");

        Assert.Equal(6, fields.Rows.Count);
        Assert.Equal("Second", fields.Rows[0][0].Value);
    }

    [Fact]
    public async Task Rejects_an_unknown_time_zone()
    {
        ToolResult result = await ParseAsync("0 * * * *", zone: "Mars/Olympus_Mons");

        Assert.False(result.Succeeded);
        Assert.Contains("time zone", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accepts_an_iana_time_zone()
    {
        ToolResult result = await ParseAsync("0 3 * * *", zone: "Europe/Dublin");

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task An_impossible_date_reports_no_occurrences()
    {
        // 30 February never happens.
        ToolResult result = await ParseAsync("0 0 30 2 *");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Status == ResultStatus.Warning);
    }
}
