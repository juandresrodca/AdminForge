using System.Globalization;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;
using Cronos;

namespace AdminForge.Tools.Ops.CronParser;

/// <summary>
/// Explains a cron expression field by field and lists when it will actually fire,
/// in the time zone you choose.
/// </summary>
public sealed class CronParserTool : ITool, IToolHandler<CronParserInput>
{
    private static readonly string[] StandardFields = ["Minute", "Hour", "Day of month", "Month", "Day of week"];
    private static readonly string[] SecondsFields = ["Second", "Minute", "Hour", "Day of month", "Month", "Day of week"];

    private static readonly Dictionary<string, string> Macros = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@yearly"] = "0 0 1 1 *",
        ["@annually"] = "0 0 1 1 *",
        ["@monthly"] = "0 0 1 * *",
        ["@weekly"] = "0 0 * * 0",
        ["@daily"] = "0 0 * * *",
        ["@midnight"] = "0 0 * * *",
        ["@hourly"] = "0 * * * *",
    };

    /// <inheritdoc />
    public string Id => "cron-parser";

    /// <inheritdoc />
    public string Name => "Cron expression parser";

    /// <inheritdoc />
    public string Description => "Explain what a cron expression means and list exactly when it will next run";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Ops;

    /// <inheritdoc />
    public string Icon => "clock";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["crontab", "schedule", "cron", "next run", "quartz", "timer", "job", "kubernetes cronjob"];

    /// <inheritdoc />
    public string? Notes =>
        "Occurrences are computed in the time zone you pick, so a daylight-saving transition shows up in the list "
        + "as a skipped or repeated run — which is exactly the surprise that breaks nightly jobs twice a year. "
        + "Pure arithmetic: nothing leaves the instance.";

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(CronParserInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        string raw = input.Expression.Trim();

        if (raw.Length == 0)
        {
            return Task.FromResult(ToolResult.Fail("Enter a cron expression, for example 30 2 * * 1-5."));
        }

        string expanded = Macros.TryGetValue(raw, out string? macro) ? macro : raw;
        string[] fields = expanded.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length is not (5 or 6))
        {
            return Task.FromResult(ToolResult.Fail(
                $"A cron expression needs five fields, or six when it includes seconds. This one has {fields.Length}."));
        }

        CronFormat format = fields.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        CronExpression expression;

        try
        {
            expression = CronExpression.Parse(expanded, format);
        }
        catch (CronFormatException ex)
        {
            return Task.FromResult(ToolResult.Fail($"That is not a valid cron expression: {ex.Message}"));
        }

        if (!TryResolveTimeZone(input.TimeZone, out TimeZoneInfo zone, out string? zoneError))
        {
            return Task.FromResult(ToolResult.Fail(zoneError!));
        }

        ResultBuilder result = ToolResult.Build();

        DateTimeOffset? next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, zone);

        result.Status(
            next is null ? ResultStatus.Warning : ResultStatus.Ok,
            next is null
                ? "This expression will never fire"
                : $"Next run {Humanise(next.Value, zone)}",
            next is null
                ? "The day-of-month and month fields describe a date that does not occur, such as 30 February."
                : $"{expanded} — evaluated in {zone.Id}");

        string[] names = fields.Length == 6 ? SecondsFields : StandardFields;

        result.Table(
            "Fields",
            ["Field", "Value", "Meaning"],
            fields.Select(IReadOnlyList<TableCell> (value, index) =>
            [
                new TableCell(names[index]),
                new TableCell(value, Monospace: true),
                new TableCell(DescribeField(names[index], value)),
            ]).ToList());

        int wanted = Math.Clamp(input.Occurrences, 1, 50);
        var rows = new List<IReadOnlyList<TableCell>>(wanted);
        DateTimeOffset cursor = DateTimeOffset.UtcNow;
        DateTimeOffset? previous = null;

        for (int i = 0; i < wanted; i++)
        {
            DateTimeOffset? occurrence = expression.GetNextOccurrence(cursor, zone);

            if (occurrence is null)
            {
                break;
            }

            TimeSpan? gap = previous is null ? null : occurrence.Value - previous.Value;

            rows.Add(
            [
                new TableCell(TimeZoneInfo.ConvertTime(occurrence.Value, zone)
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Monospace: true),
                new TableCell(TimeZoneInfo.ConvertTime(occurrence.Value, zone)
                    .ToString("dddd", CultureInfo.InvariantCulture)),
                new TableCell(occurrence.Value.UtcDateTime
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Monospace: true),
                new TableCell(gap is null ? "—" : DescribeGap(gap.Value), Monospace: true),
            ]);

            previous = occurrence;
            cursor = occurrence.Value;
        }

        result.Table(
            $"Next {rows.Count} run{(rows.Count == 1 ? "" : "s")}",
            [$"Local ({zone.Id})", "Day", "UTC", "Gap"],
            rows,
            "This expression has no upcoming occurrences.");

        if (Macros.ContainsKey(raw))
        {
            result.KeyValues("Macro", kv => kv
                .Add("You entered", raw, monospace: true)
                .Add("Equivalent to", expanded, monospace: true));
        }

        return Task.FromResult(result.ToResult());
    }

    private static bool TryResolveTimeZone(string? id, out TimeZoneInfo zone, out string? error)
    {
        error = null;
        string wanted = string.IsNullOrWhiteSpace(id) ? "UTC" : id.Trim();

        try
        {
            // .NET resolves IANA and Windows ids on both platforms, so Europe/Dublin and
            // "GMT Standard Time" both work regardless of where the container runs.
            zone = TimeZoneInfo.FindSystemTimeZoneById(wanted);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            error = $"'{wanted}' is not a time zone this machine knows. Try an IANA name such as Europe/Dublin.";
            return false;
        }
    }

    private static string Humanise(DateTimeOffset occurrence, TimeZoneInfo zone)
    {
        TimeSpan until = occurrence - DateTimeOffset.UtcNow;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(occurrence, zone);
        string stamp = local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return until.TotalSeconds switch
        {
            < 60 => $"in under a minute, at {stamp}",
            < 3600 => $"in {until.TotalMinutes:N0} minutes, at {stamp}",
            < 86400 => $"in {until.TotalHours:N0} hours, at {stamp}",
            _ => $"in {until.TotalDays:N0} days, at {stamp}",
        };
    }

    private static string DescribeGap(TimeSpan gap) => gap.TotalSeconds switch
    {
        < 60 => $"{gap.TotalSeconds:N0}s",
        < 3600 => $"{gap.TotalMinutes:N0}m",
        < 86400 => $"{gap.TotalHours:N1}h",
        _ => $"{gap.TotalDays:N1}d",
    };

    private static string DescribeField(string name, string value)
    {
        if (value == "*")
        {
            return $"Every {name.ToLowerInvariant()}";
        }

        if (value.StartsWith("*/", StringComparison.Ordinal))
        {
            return $"Every {value[2..]} {name.ToLowerInvariant()} units, starting at the first";
        }

        if (value.Contains('/', StringComparison.Ordinal))
        {
            string[] parts = value.Split('/', 2);
            return $"Every {parts[1]}, within {parts[0]}";
        }

        if (value.Contains(',', StringComparison.Ordinal))
        {
            return $"Only at {value.Replace(",", ", ", StringComparison.Ordinal)}";
        }

        if (value.Contains('-', StringComparison.Ordinal))
        {
            string[] parts = value.Split('-', 2);
            return $"From {parts[0]} through {parts[1]}";
        }

        return value switch
        {
            "?" => "No specific value",
            "L" => "The last one",
            _ => $"Only at {value}",
        };
    }
}
