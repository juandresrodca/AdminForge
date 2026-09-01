using System.Globalization;
using AdminForge.Core.Results;
using AdminForge.Tools.Windows.ErrorCode;

namespace AdminForge.Tests;

/// <summary>Code parsing, and the integrity of the bundled dataset.</summary>
public sealed class WindowsErrorCodeTests
{
    private static readonly WindowsErrorCodeTool Tool = new();

    private static async Task<ToolResult> LookupAsync(string code) =>
        await Tool.ExecuteAsync(new WindowsErrorCodeInput { Code = code }, CancellationToken.None);

    [Theory]
    [InlineData("0x80070005", 0x80070005u)]
    [InlineData("0X80070005", 0x80070005u)]
    [InlineData("80070005", 0x80070005u)]
    [InlineData("2147942405", 2147942405u)]
    [InlineData("-2147024891", 0x80070005u)]
    [InlineData("5", 5u)]
    [InlineData("0xC0000022", 0xC0000022u)]
    [InlineData("C0000022", 0xC0000022u)]
    [InlineData("0x 8007 0005", 0x80070005u)]
    public void Parses_every_form_an_admin_would_paste(string input, uint expected)
    {
        Assert.True(WindowsErrorCodeTool.TryParseCode(input, out uint value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ERROR_ACCESS_DENIED")]
    [InlineData("not a code")]
    public void Rejects_things_that_are_not_numbers(string input) =>
        Assert.False(WindowsErrorCodeTool.TryParseCode(input, out _));

    [Fact]
    public async Task Resolves_a_win32_error_wrapped_as_an_hresult()
    {
        ToolResult result = await LookupAsync("0x80070005");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Message == "ERROR_ACCESS_DENIED");

        // The decomposition must name the facility that produced it.
        Assert.Contains(
            result.Blocks.OfType<KeyValueBlock>().SelectMany(b => b.Rows),
            r => r.Label == "Facility" && r.Value.Contains("FACILITY_WIN32", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolves_a_bare_win32_code()
    {
        ToolResult result = await LookupAsync("5");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Message == "ERROR_ACCESS_DENIED");
    }

    [Fact]
    public async Task Resolves_an_ntstatus_and_says_so()
    {
        ToolResult result = await LookupAsync("0xC000006A");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Message == "STATUS_WRONG_PASSWORD");
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Title == "Note");
    }

    [Fact]
    public async Task Finds_a_code_by_part_of_its_symbolic_name()
    {
        ToolResult result = await LookupAsync("ACCESS_DENIED");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task An_unknown_code_still_gets_decomposed()
    {
        ToolResult result = await LookupAsync("0x87654321");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Blocks.OfType<StatusBlock>(), b => b.Status == ResultStatus.Warning);
        Assert.Contains(result.Blocks.OfType<KeyValueBlock>(), b => b.Title == "HRESULT decomposition");
    }

    [Fact]
    public async Task An_empty_code_fails_with_a_message()
    {
        ToolResult result = await LookupAsync("   ");

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void The_dataset_loads_and_is_not_empty() =>
        Assert.NotEmpty(WindowsErrorCodeTool.Entries);

    [Fact]
    public void Every_dataset_entry_is_complete_and_well_formed()
    {
        foreach (WindowsErrorEntry entry in WindowsErrorCodeTool.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Symbol), $"{entry.Code}: symbol is empty.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Message), $"{entry.Code}: message is empty.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Source), $"{entry.Code}: source is empty.");

            Assert.True(
                entry.Code.StartsWith("0x", StringComparison.Ordinal),
                $"{entry.Symbol}: write the code as 0x-prefixed hex so the dataset stays consistent.");

            Assert.True(
                WindowsErrorCodeTool.TryParseCode(entry.Code, out _),
                $"{entry.Symbol}: '{entry.Code}' does not parse as a 32-bit value.");

            Assert.True(
                entry.Code.Length == 10,
                $"{entry.Symbol}: pad the code to eight hex digits, e.g. 0x00000005.");

            Assert.Equal(entry.Code.ToUpperInvariant().Replace("0X", "0x", StringComparison.Ordinal), entry.Code);
        }
    }

    [Fact]
    public void The_dataset_has_no_duplicate_codes()
    {
        var duplicates = WindowsErrorCodeTool.Entries
            .GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({string.Join(" / ", g.Select(e => e.Symbol))})")
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate codes in the dataset: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void The_dataset_is_sorted_by_code()
    {
        // Sorted order keeps additions reviewable: a new entry shows as one added line
        // rather than a diff that moves half the file.
        var codes = WindowsErrorCodeTool.Entries
            .Select(e => uint.Parse(e.Code[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(codes.OrderBy(c => c).ToList(), codes);
    }
}
