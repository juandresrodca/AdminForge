using AdminForge.Core.Forms;

namespace AdminForge.Tools.Ops.CronParser;

/// <summary>Input for the cron expression parser.</summary>
public sealed class CronParserInput
{
    /// <summary>The cron expression to explain.</summary>
    [ToolField("Cron expression",
        Placeholder = "30 2 * * 1-5",
        Required = true,
        MaxLength = 200,
        Help = "Five fields (minute hour day month weekday) or six, where the first is seconds. "
               + "Macros such as @daily and @weekly work too.")]
    public string Expression { get; set; } = string.Empty;

    /// <summary>The time zone the schedule is evaluated in.</summary>
    [ToolField("Time zone",
        Placeholder = "UTC",
        Half = true,
        MaxLength = 80,
        Help = "An IANA name like Europe/Dublin, or a Windows name. Defaults to UTC.")]
    public string TimeZone { get; set; } = "UTC";

    /// <summary>How many upcoming runs to list.</summary>
    [ToolField("Next runs to show",
        Kind = FieldKind.Number,
        Min = 1,
        Max = 50,
        Half = true)]
    public int Occurrences { get; set; } = 10;
}
