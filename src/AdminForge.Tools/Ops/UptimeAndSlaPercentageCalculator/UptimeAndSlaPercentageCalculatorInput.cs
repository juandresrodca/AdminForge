using AdminForge.Core.Forms;

namespace AdminForge.Tools.Ops.UptimeAndSlaPercentageCalculator;

/// <summary>The period an availability target is measured over.</summary>
public enum SlaWindow
{
    /// <summary>One day.</summary>
    Day,

    /// <summary>Seven days.</summary>
    Week,

    /// <summary>An average month: a 365.25-day year divided by twelve.</summary>
    Month,

    /// <summary>An average quarter: a 365.25-day year divided by four.</summary>
    Quarter,

    /// <summary>A 365.25-day year.</summary>
    Year,
}

/// <summary>The unit a downtime figure is entered in.</summary>
public enum DowntimeUnit
{
    /// <summary>Seconds.</summary>
    Seconds,

    /// <summary>Minutes.</summary>
    Minutes,

    /// <summary>Hours.</summary>
    Hours,

    /// <summary>Days.</summary>
    Days,
}

/// <summary>Input for the uptime and SLA calculator.</summary>
public sealed class UptimeAndSlaPercentageCalculatorInput
{
    /// <summary>The availability target, as a percentage.</summary>
    /// <remarks>Text rather than a number field, so decimals such as 99.95 are accepted.</remarks>
    [ToolField("Availability target (%)",
        Placeholder = "99.9",
        MaxLength = 20,
        Half = true,
        Help = "Leave empty to work it out from an allowed downtime instead.")]
    public string? Availability { get; set; }

    /// <summary>The period the target is measured over.</summary>
    [ToolField("Window", Half = true)]
    public SlaWindow Window { get; set; } = SlaWindow.Month;

    /// <summary>Downtime allowed per window, as an alternative to a percentage.</summary>
    [ToolField("Allowed downtime",
        Placeholder = "43.8",
        MaxLength = 20,
        Half = true,
        Help = "Use instead of a percentage to find the availability it implies.")]
    public string? AllowedDowntime { get; set; }

    /// <summary>The unit for both downtime fields.</summary>
    [ToolField("Downtime unit", Half = true)]
    public DowntimeUnit Unit { get; set; } = DowntimeUnit.Minutes;

    /// <summary>Downtime already incurred in the window.</summary>
    [ToolField("Actual downtime",
        Placeholder = "12",
        MaxLength = 20,
        Help = "Optional. Downtime so far in this window, to check it against the target.")]
    public string? ActualDowntime { get; set; }
}
