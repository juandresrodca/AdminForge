using System.Globalization;

namespace AdminForge.Tools.Ops.UptimeAndSlaPercentageCalculator;

/// <summary>
/// Availability arithmetic. Works in <see cref="decimal"/> so that a percentage and the
/// downtime it allows convert back and forth exactly, with no floating-point drift.
/// </summary>
public static class SlaMath
{
    /// <summary>Seconds in a 365.25-day year, the average that absorbs leap years.</summary>
    public const decimal SecondsPerYear = 365.25m * 86_400;

    private static readonly string[] Numbers = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    /// <summary>Length of a window in seconds. Months and quarters are averages of the year.</summary>
    /// <param name="window">The window.</param>
    public static decimal WindowSeconds(SlaWindow window) => window switch
    {
        SlaWindow.Day => 86_400,
        SlaWindow.Week => 7 * 86_400,
        SlaWindow.Month => SecondsPerYear / 12,
        SlaWindow.Quarter => SecondsPerYear / 4,
        SlaWindow.Year => SecondsPerYear,
        _ => throw new ArgumentOutOfRangeException(nameof(window)),
    };

    /// <summary>Convert a downtime figure to seconds.</summary>
    /// <param name="value">The amount.</param>
    /// <param name="unit">The unit it is in.</param>
    public static decimal ToSeconds(decimal value, DowntimeUnit unit) => unit switch
    {
        DowntimeUnit.Seconds => value,
        DowntimeUnit.Minutes => value * 60,
        DowntimeUnit.Hours => value * 3_600,
        DowntimeUnit.Days => value * 86_400,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    /// <summary>Downtime, in seconds, that an availability percentage allows over a window.</summary>
    /// <param name="availability">The target, from 0 to 100.</param>
    /// <param name="window">The window.</param>
    public static decimal AllowedDowntime(decimal availability, SlaWindow window) =>
        (100 - availability) / 100 * WindowSeconds(window);

    /// <summary>Availability percentage achieved with the given downtime over a window.</summary>
    /// <param name="downtimeSeconds">Downtime in seconds, no longer than the window.</param>
    /// <param name="window">The window.</param>
    public static decimal Availability(decimal downtimeSeconds, SlaWindow window) =>
        100 - (downtimeSeconds * 100 / WindowSeconds(window));

    /// <summary>The same share of downtime over a different window, e.g. a monthly budget as a yearly one.</summary>
    /// <param name="downtimeSeconds">Downtime over <paramref name="from"/>, in seconds.</param>
    /// <param name="from">The window the downtime was given for.</param>
    /// <param name="to">The window to express it in.</param>
    public static decimal Rescale(decimal downtimeSeconds, SlaWindow from, SlaWindow to) =>
        downtimeSeconds * WindowSeconds(to) / WindowSeconds(from);

    /// <summary>The familiar name for an availability level, such as "three nines" for 99.9%.</summary>
    /// <param name="availability">The percentage, from 0 to 100.</param>
    public static string NinesName(decimal availability)
    {
        if (availability >= 100)
        {
            return "100%, no downtime at all";
        }

        // Each nine divides the permitted downtime by ten: 10%, then 1%, then 0.1%.
        decimal gap = 100 - availability;
        decimal step = 10;
        int nines = 0;

        while (gap <= step)
        {
            nines++;
            step /= 10;
        }

        if (nines == 0)
        {
            return "fewer than one nine";
        }

        if (gap == step * 10)
        {
            return nines == 1 ? "one nine" : $"{Count(nines)} nines";
        }

        return gap == step * 5
            ? $"{Count(nines)} and a half nines"
            : $"between {Count(nines)} and {Count(nines + 1)} nines";
    }

    /// <summary>
    /// A readable duration such as "8h 45m 57s". Rounded down, so a downtime budget is never
    /// overstated; under a minute it keeps a tenth of a second.
    /// </summary>
    /// <param name="seconds">The duration in seconds, zero or more.</param>
    public static string FormatDuration(decimal seconds)
    {
        if (seconds < 60)
        {
            return (decimal.Floor(seconds * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture) + "s";
        }

        TimeSpan time = TimeSpan.FromSeconds((long)seconds);

        string[] parts =
        [
            time.Days > 0 ? $"{time.Days}d" : "",
            time.Hours > 0 ? $"{time.Hours}h" : "",
            time.Minutes > 0 ? $"{time.Minutes}m" : "",
            time.Seconds > 0 ? $"{time.Seconds}s" : "",
        ];

        return string.Join(' ', parts.Where(part => part.Length > 0));
    }

    private static string Count(int number) =>
        number <= Numbers.Length ? Numbers[number - 1] : number.ToString(CultureInfo.InvariantCulture);
}
