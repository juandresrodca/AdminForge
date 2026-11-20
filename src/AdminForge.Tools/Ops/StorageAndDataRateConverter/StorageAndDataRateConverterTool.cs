using System.Globalization;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Ops.StorageAndDataRateConverter;

/// <summary>Converts storage sizes and rates and estimates transfers at a constant rate.</summary>
public sealed class StorageAndDataRateConverterTool : ITool, IToolHandler<StorageAndDataRateConverterInput>
{
    private static readonly Unit Bytes = new("B", 1);
    private static readonly (Unit Decimal, Unit Binary)[] StorageUnits =
    [
        (new("KB", 1e3), new("KiB", 1L << 10)),
        (new("MB", 1e6), new("MiB", 1L << 20)),
        (new("GB", 1e9), new("GiB", 1L << 30)),
        (new("TB", 1e12), new("TiB", 1L << 40)),
        (new("PB", 1e15), new("PiB", 1L << 50)),
    ];
    private static readonly Unit[] BitRates =
    [
        new("bps", 1d / 8),
        new("Kbps", 1e3 / 8),
        new("Mbps", 1e6 / 8),
        new("Gbps", 1e9 / 8),
    ];
    private static readonly Unit[] ByteRates = [new("KB/s", 1e3), new("MB/s", 1e6), new("GB/s", 1e9)];
    private const string RangeError = "The result is too large or too small to represent. Try a different value.";

    /// <inheritdoc />
    public string Id => "storage-and-data-rate-converter";

    /// <inheritdoc />
    public string Name => "Storage and data-rate converter";

    /// <inheritdoc />
    public string Description => "Convert decimal and binary storage units, switch between bit and byte rates, and estimate transfer times";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Ops;

    /// <inheritdoc />
    public string Icon => "gauge";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords => ["tb", "tib", "kib", "mbps", "bandwidth", "throughput", "transfer time"];

    /// <inheritdoc />
    public string? Notes =>
        "Decimal prefixes use powers of 1,000 and binary prefixes use powers of 1,024. "
        + "Lowercase b means bits, uppercase B means bytes, and one byte is eight bits. "
        + "Transfer estimates assume a constant rate.";

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(StorageAndDataRateConverterInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Task.FromResult(Calculate(input));
    }

    private static ToolResult Calculate(StorageAndDataRateConverterInput input)
    {
        if (!TryReadNumber(input.Value, out double value))
        {
            return ToolResult.Fail("Enter a non-negative number, such as 4 or 0.5.");
        }

        if (FindStorageUnit(input.Unit) is Unit storage)
        {
            if (!string.IsNullOrWhiteSpace(input.TransferSize) || !string.IsNullOrWhiteSpace(input.TransferSeconds))
            {
                return ToolResult.Fail("Choose a data-rate unit to estimate a transfer.");
            }

            return ConvertStorage(value, storage);
        }

        Unit? rate = BitRates.FirstOrDefault(unit => unit.Symbol == input.Unit)
            ?? ByteRates.FirstOrDefault(unit => unit.Symbol == input.Unit);
        return rate is null
            ? ToolResult.Fail("Choose a storage or data-rate unit. Use b for bits and B for bytes.")
            : ConvertRate(value, rate, input);
    }

    private static ToolResult ConvertStorage(double value, Unit source)
    {
        double bytes = Convert(value, source, Bytes);
        if (!IsRepresentable(bytes, value))
        {
            return ToolResult.Fail(RangeError);
        }

        var rows = new List<IReadOnlyList<TableCell>>(StorageUnits.Length);
        foreach ((Unit decimalUnit, Unit binaryUnit) in StorageUnits)
        {
            double decimalValue = Convert(value, source, decimalUnit);
            double binaryValue = Convert(value, source, binaryUnit);
            if (!IsRepresentable(decimalValue, value) || !IsRepresentable(binaryValue, value))
            {
                return ToolResult.Fail(RangeError);
            }

            double difference = 1 - (decimalUnit.Factor / binaryUnit.Factor);
            rows.Add(
            [
                FormatQuantity(decimalValue, decimalUnit.Symbol),
                FormatQuantity(binaryValue, binaryUnit.Symbol),
                new TableCell(value == 0 ? "—" : difference.ToString("P2", CultureInfo.InvariantCulture)),
            ]);
        }

        return ToolResult.Build()
            .KeyValues("Storage", kv => kv.Add("Bytes", FormatNumber(bytes), monospace: true))
            .Table("Storage equivalents", ["Decimal", "Binary", "Binary value lower by"], rows)
            .ToResult();
    }

    private static ToolResult ConvertRate(double value, Unit source, StorageAndDataRateConverterInput input)
    {
        var rows = new List<IReadOnlyList<TableCell>>(BitRates.Length + ByteRates.Length);
        foreach (Unit unit in BitRates.Concat(ByteRates))
        {
            double converted = Convert(value, source, unit);
            if (!IsRepresentable(converted, value))
            {
                return ToolResult.Fail(RangeError);
            }

            rows.Add([new TableCell(unit.Symbol), new TableCell(FormatNumber(converted), Monospace: true)]);
        }

        ResultBuilder result = ToolResult.Build().Table("Data-rate equivalents", ["Unit", "Value"], rows);
        var estimates = new List<KeyValueRow>();
        if (!string.IsNullOrWhiteSpace(input.TransferSize))
        {
            if (!TryReadNumber(input.TransferSize, out double size) || FindStorageUnit(input.TransferSizeUnit) is not Unit sizeUnit)
            {
                return ToolResult.Fail("Enter a non-negative transfer size and choose its storage unit.");
            }

            if (value == 0)
            {
                return ToolResult.Fail("Enter a data rate greater than zero to calculate a transfer time.");
            }

            double seconds = (size / value) * (sizeUnit.Factor / source.Factor);
            if (!IsRepresentable(seconds, size))
            {
                seconds = size / Convert(value, source, sizeUnit);
            }
            if (!IsRepresentable(seconds, size))
            {
                return ToolResult.Fail(RangeError);
            }

            estimates.Add(new KeyValueRow("Estimated transfer time", FormatNumber(seconds) + " s", Monospace: true));
        }

        if (!string.IsNullOrWhiteSpace(input.TransferSeconds))
        {
            if (!TryReadNumber(input.TransferSeconds, out double seconds))
            {
                return ToolResult.Fail("Enter a non-negative duration in seconds.");
            }

            double size = Convert(value, source, Bytes) * seconds;
            if (!IsRepresentable(size, value == 0 ? 0 : seconds))
            {
                return ToolResult.Fail(RangeError);
            }

            estimates.Add(new KeyValueRow("Estimated transferable size", FormatNumber(size) + " B", Monospace: true));
        }

        return estimates.Count == 0 ? result.ToResult() : result.KeyValues("Transfer estimates", estimates).ToResult();
    }

    private static Unit? FindStorageUnit(string symbol)
    {
        if (symbol == Bytes.Symbol)
        {
            return Bytes;
        }

        foreach ((Unit decimalUnit, Unit binaryUnit) in StorageUnits)
        {
            if (symbol == decimalUnit.Symbol)
            {
                return decimalUnit;
            }
            if (symbol == binaryUnit.Symbol)
            {
                return binaryUnit;
            }
        }

        return null;
    }

    private static bool TryReadNumber(string input, out double value)
    {
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || !double.IsFinite(value) || value < 0)
        {
            return false;
        }

        if (value != 0)
        {
            return true;
        }

        int exponent = input.IndexOfAny(['e', 'E']);
        ReadOnlySpan<char> significand = exponent < 0 ? input.AsSpan() : input.AsSpan(0, exponent);
        return significand.IndexOfAnyInRange('1', '9') < 0;
    }

    private static bool IsRepresentable(double result, double input) => double.IsFinite(result) && (result != 0 || input == 0);

    private static double Convert(double value, Unit from, Unit to) => value * (from.Factor / to.Factor);

    private static TableCell FormatQuantity(double value, string unit) => new(FormatNumber(value) + " " + unit, Monospace: true);

    private static string FormatNumber(double value)
    {
        if (value == 0)
        {
            return "0";
        }

        string formatted = value.ToString("G15", CultureInfo.InvariantCulture);
        int separator = formatted.IndexOf('E');
        if (separator < 0)
        {
            return formatted;
        }

        int point = int.Parse(formatted.AsSpan(separator + 1), CultureInfo.InvariantCulture) + 1;
        string digits = formatted[..separator].Replace(".", "", StringComparison.Ordinal);
        if (point <= 0)
        {
            return "0." + new string('0', -point) + digits;
        }
        return point >= digits.Length ? digits.PadRight(point, '0') : digits.Insert(point, ".");
    }

    private sealed record Unit(string Symbol, double Factor);
}
