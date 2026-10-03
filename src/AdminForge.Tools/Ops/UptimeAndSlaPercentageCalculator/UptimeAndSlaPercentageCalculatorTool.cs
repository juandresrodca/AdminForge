using System.Globalization;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Ops.UptimeAndSlaPercentageCalculator;

/// <summary>
/// Converts between an availability target and the downtime it allows, in every window at
/// once, and checks downtime already incurred against the target. "Three nines" sounds
/// precise until someone asks how many minutes a month that is.
/// </summary>
public sealed class UptimeAndSlaPercentageCalculatorTool : ITool, IToolHandler<UptimeAndSlaPercentageCalculatorInput>
{
    private static readonly decimal[] Tiers = [99m, 99.5m, 99.9m, 99.95m, 99.99m, 99.999m];

    /// <inheritdoc />
    public string Id => "uptime-and-sla-percentage-calculator";

    /// <inheritdoc />
    public string Name => "Uptime and SLA calculator";

    /// <inheritdoc />
    public string Description => "Convert an availability target into the downtime it allows, and downtime back into availability";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Ops;

    /// <inheritdoc />
    public string Icon => "gauge";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["sla", "slo", "uptime", "availability", "nines", "downtime", "error budget", "99.9"];

    /// <inheritdoc />
    public string? Notes =>
        "Months and quarters are averages of a 365.25-day year: a month is 30.4375 days and a quarter 91.3125, "
        + "so twelve months make exactly one year. Durations are rounded down to the second (to a tenth of a "
        + "second under a minute), so a downtime budget is never overstated. Pure arithmetic: nothing leaves the instance.";

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(UptimeAndSlaPercentageCalculatorInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Task.FromResult(Calculate(input));
    }

    private static ToolResult Calculate(UptimeAndSlaPercentageCalculatorInput input)
    {
        decimal window = SlaMath.WindowSeconds(input.Window);
        string per = input.Window.ToString().ToLowerInvariant();

        if (!TryReadPercent(input.Availability, out decimal? target))
        {
            return ToolResult.Fail("The availability target must be a percentage from 0 to 100, such as 99.9.");
        }

        if (!TryReadDowntime(input.AllowedDowntime, input.Unit, window, out decimal? allowed)
            || !TryReadDowntime(input.ActualDowntime, input.Unit, window, out decimal? actual))
        {
            return ToolResult.Fail(
                $"Downtime must be a number of {input.Unit.ToString().ToLowerInvariant()}, from 0 up to the length of one {per}.");
        }

        if ((target is null) == (allowed is null))
        {
            return ToolResult.Fail(target is null
                ? "Enter an availability target such as 99.9, or an allowed downtime to work backwards from."
                : "Enter an availability target or an allowed downtime, not both.");
        }

        decimal availability = target ?? SlaMath.Availability(allowed!.Value, input.Window);
        decimal budget = allowed ?? SlaMath.AllowedDowntime(availability, input.Window);

        ResultBuilder result = ToolResult.Build()
            .Status(
                ResultStatus.Info,
                $"{Percent(availability)} allows {SlaMath.FormatDuration(budget)} of downtime per {per}",
                SlaMath.NinesName(availability))
            .Table(
                "Allowed downtime",
                ["Per", "Downtime"],
                Enum.GetValues<SlaWindow>().Select(IReadOnlyList<TableCell> (each) =>
                [
                    new TableCell(each.ToString(), each == input.Window ? ResultStatus.Info : ResultStatus.Neutral),
                    Duration(SlaMath.Rescale(budget, input.Window, each)),
                ]).ToList());

        if (actual is decimal spent)
        {
            decimal left = budget - spent;

            result.Status(
                left >= 0 ? ResultStatus.Ok : ResultStatus.Danger,
                left >= 0
                    ? $"Target met, with {SlaMath.FormatDuration(left)} of downtime budget left"
                    : $"Target missed, over budget by {SlaMath.FormatDuration(-left)}",
                $"{SlaMath.FormatDuration(spent)} of downtime is {Percent(SlaMath.Availability(spent, input.Window))} availability this {per}",
                "Actual downtime");
        }

        return result
            .Table(
                "Common tiers",
                ["Availability", "Nines", "Per day", "Per month", "Per year"],
                Tiers.Select(IReadOnlyList<TableCell> (tier) =>
                [
                    new TableCell(Percent(tier), Monospace: true),
                    new TableCell(SlaMath.NinesName(tier)),
                    Duration(SlaMath.AllowedDowntime(tier, SlaWindow.Day)),
                    Duration(SlaMath.AllowedDowntime(tier, SlaWindow.Month)),
                    Duration(SlaMath.AllowedDowntime(tier, SlaWindow.Year)),
                ]).ToList())
            .ToResult();
    }

    private static TableCell Duration(decimal seconds) => new(SlaMath.FormatDuration(seconds), Monospace: true);

    private static string Percent(decimal value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture) + "%";

    private static bool TryReadPercent(string? text, out decimal? percent) =>
        TryRead(text?.Trim().TrimEnd('%'), 100, out percent);

    private static bool TryReadDowntime(string? text, DowntimeUnit unit, decimal window, out decimal? seconds)
    {
        // The ceiling is the window expressed in the user's unit, so nothing can overflow.
        bool valid = TryRead(text, window / SlaMath.ToSeconds(1, unit), out decimal? value);
        seconds = value is decimal amount ? SlaMath.ToSeconds(amount, unit) : null;

        return valid;
    }

    /// <summary>
    /// Empty text is valid and reads as null; anything else must be a plain number from 0 to max.
    /// Thousands separators are refused, so "1,5" is rejected rather than read as fifteen.
    /// </summary>
    private static bool TryRead(string? text, decimal max, out decimal? value)
    {
        const NumberStyles Plain = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

        value = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!decimal.TryParse(text, Plain, CultureInfo.InvariantCulture, out decimal number) || number > max)
        {
            return false;
        }

        value = number;
        return true;
    }
}
