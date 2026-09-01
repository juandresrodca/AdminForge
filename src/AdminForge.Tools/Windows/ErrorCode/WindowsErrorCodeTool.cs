using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdminForge.Core.Forms;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Windows.ErrorCode;

/// <summary>One entry in the bundled error-code dataset.</summary>
/// <param name="Code">The code in 0x-prefixed hex.</param>
/// <param name="Symbol">The symbolic constant name.</param>
/// <param name="Source">Which family the code belongs to.</param>
/// <param name="Message">The system message text.</param>
/// <param name="Hint">Optional practical advice for an administrator.</param>
public sealed record WindowsErrorEntry(
    string Code,
    string Symbol,
    string Source,
    string Message,
    string? Hint = null);

/// <summary>Input for the Windows error code lookup.</summary>
public sealed class WindowsErrorCodeInput
{
    /// <summary>The code or symbolic name to look up.</summary>
    [ToolField("Error code",
        Placeholder = "0x80070005",
        Required = true,
        MaxLength = 80,
        Help = "Hex (0x80070005 or 80070005), decimal (-2147024891 or 5), or a symbolic name such as "
               + "ERROR_ACCESS_DENIED. An unprefixed eight-digit value is read as hex.")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Decodes Windows error codes: HRESULT, Win32, NTSTATUS, MSI exit codes, Windows
/// Update and servicing failures, and the Intune codes that turn up in deployment
/// reports.
/// </summary>
public sealed class WindowsErrorCodeTool : ITool, IToolHandler<WindowsErrorCodeInput>
{
    private static readonly Lazy<IReadOnlyList<WindowsErrorEntry>> Dataset = new(LoadDataset);

    /// <summary>The HRESULT facility codes worth naming.</summary>
    private static readonly Dictionary<int, string> Facilities = new()
    {
        [0] = "FACILITY_NULL — general",
        [1] = "FACILITY_RPC",
        [2] = "FACILITY_DISPATCH — IDispatch",
        [3] = "FACILITY_STORAGE — structured storage",
        [4] = "FACILITY_ITF — interface-specific",
        [7] = "FACILITY_WIN32 — a Win32 error wrapped as an HRESULT",
        [8] = "FACILITY_WINDOWS",
        [9] = "FACILITY_SECURITY / SSPI",
        [10] = "FACILITY_CONTROL",
        [11] = "FACILITY_CERT — certificates",
        [12] = "FACILITY_INTERNET",
        [13] = "FACILITY_MEDIASERVER",
        [14] = "FACILITY_MSMQ",
        [15] = "FACILITY_SETUPAPI",
        [16] = "FACILITY_SCARD — smart cards",
        [17] = "FACILITY_COMPLUS",
        [18] = "FACILITY_AAF",
        [19] = "FACILITY_URT — .NET runtime",
        [20] = "FACILITY_ACS",
        [21] = "FACILITY_DPLAY",
        [22] = "FACILITY_UMI",
        [23] = "FACILITY_SXS — side-by-side assemblies",
        [24] = "FACILITY_WINDOWS_CE",
        [25] = "FACILITY_HTTP",
        [26] = "FACILITY_USERMODE_COMMONLOG",
        [31] = "FACILITY_USERMODE_FILTER_MANAGER",
        [32] = "FACILITY_BACKGROUNDCOPY — BITS",
        [33] = "FACILITY_CONFIGURATION — Windows Update and CBS",
        [34] = "FACILITY_STATE_MANAGEMENT",
        [35] = "FACILITY_METADIRECTORY",
        [36] = "FACILITY_WINDOWSUPDATE",
        [37] = "FACILITY_DIRECTORYSERVICE",
        [38] = "FACILITY_GRAPHICS",
        [39] = "FACILITY_SHELL",
    };

    /// <inheritdoc />
    public string Id => "windows-error-code";

    /// <inheritdoc />
    public string Name => "Windows error code lookup";

    /// <inheritdoc />
    public string Description => "Decode an HRESULT, Win32, NTSTATUS, MSI, Windows Update or Intune error code";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Windows;

    /// <inheritdoc />
    public string Icon => "windows";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["hresult", "ntstatus", "win32", "error", "0x80070005", "msi", "exit code", "wsus", "intune", "sccm", "dism"];

    /// <inheritdoc />
    public string? Notes =>
        "Even when a code is not in the dataset, the HRESULT decomposition still tells you a lot: the facility "
        + "names the component that raised it, and a FACILITY_WIN32 result carries an ordinary Win32 error in its "
        + "low sixteen bits, which is looked up automatically. The dataset lives in a JSON file — adding entries "
        + "is a pull request with no C# in it.";

    /// <summary>The bundled entries, exposed so tests can assert the dataset stays well-formed.</summary>
    public static IReadOnlyList<WindowsErrorEntry> Entries => Dataset.Value;

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(WindowsErrorCodeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        string raw = input.Code.Trim();

        if (raw.Length == 0)
        {
            return Task.FromResult(ToolResult.Fail("Enter an error code, for example 0x80070005."));
        }

        // A symbolic name is looked up directly; anything else is parsed as a number.
        if (!TryParseCode(raw, out uint value))
        {
            IReadOnlyList<WindowsErrorEntry> bySymbol = SearchSymbols(raw);

            if (bySymbol.Count == 0)
            {
                return Task.FromResult(ToolResult.Fail(
                    $"'{raw}' is not a number this tool recognises, and no symbolic name matches it. "
                    + "Try hex (0x80070005), decimal (2147942405), or part of a constant name (ACCESS_DENIED)."));
            }

            return Task.FromResult(BuildSymbolResult(raw, bySymbol));
        }

        return Task.FromResult(BuildNumericResult(value));
    }

    private static ToolResult BuildNumericResult(uint value)
    {
        ResultBuilder result = ToolResult.Build();
        string hex = "0x" + value.ToString("X8", CultureInfo.InvariantCulture);

        WindowsErrorEntry? match = Find(value);

        // A FACILITY_WIN32 HRESULT wraps an ordinary Win32 error, so fall back to that.
        int severity = (int)(value >> 31);
        int facility = (int)((value >> 16) & 0x1FFF);
        uint lowWord = value & 0xFFFF;
        bool isWin32Wrapped = severity == 1 && facility == 7;

        WindowsErrorEntry? wrapped = match is null && isWin32Wrapped ? Find(lowWord) : null;
        WindowsErrorEntry? effective = match ?? wrapped;

        if (effective is null)
        {
            result.Status(
                ResultStatus.Warning,
                $"{hex} is not in the bundled dataset",
                "The decomposition below still narrows it down. If you know what this code means, "
                + "adding it is a one-line pull request.");
        }
        else
        {
            result.Status(
                value == 0 ? ResultStatus.Ok : ResultStatus.Info,
                effective.Symbol,
                effective.Message);
        }

        result.KeyValues("The code", kv =>
        {
            kv.Add("Hexadecimal", hex, monospace: true);
            kv.Add("Unsigned decimal", value.ToString(CultureInfo.InvariantCulture), monospace: true);
            kv.Add("Signed decimal", unchecked((int)value).ToString(CultureInfo.InvariantCulture), monospace: true);
            kv.AddIf(effective is not null, "Symbolic name", effective?.Symbol, monospace: true);
            kv.AddIf(effective is not null, "Family", effective?.Source);
        });

        if (effective?.Hint is not null)
        {
            result.Status(ResultStatus.Info, "What usually causes this", effective.Hint, "In practice");
        }

        if (wrapped is not null)
        {
            result.Status(
                ResultStatus.Info,
                $"Wraps Win32 error {lowWord}",
                $"{hex} is the HRESULT form of {wrapped.Symbol}. The two are the same failure.",
                "Win32 equivalent");
        }

        AppendDecomposition(result, value, severity, facility, lowWord);

        return result.ToResult();
    }

    private static void AppendDecomposition(ResultBuilder result, uint value, int severity, int facility, uint lowWord)
    {
        bool looksLikeNtStatus = (value >> 30) == 0b11 && facility is not 7;

        result.KeyValues("HRESULT decomposition", kv =>
        {
            kv.Add("Severity bit", severity == 1 ? "1 — failure" : "0 — success",
                severity == 1 ? ResultStatus.Danger : ResultStatus.Ok, monospace: true);
            kv.Add("Customer bit", ((value >> 29) & 1) == 1 ? "1 — defined by a third party" : "0 — defined by Microsoft",
                monospace: true);
            kv.Add(
                "Facility",
                Facilities.TryGetValue(facility, out string? name)
                    ? $"{facility} — {name}"
                    : facility.ToString(CultureInfo.InvariantCulture),
                monospace: true);
            kv.Add("Code", $"0x{lowWord:X4} ({lowWord})", monospace: true);
        });

        if (looksLikeNtStatus)
        {
            result.Status(
                ResultStatus.Info,
                "This may be an NTSTATUS rather than an HRESULT",
                "Values starting 0xC are usually NTSTATUS error codes, which use a different bit layout. "
                + "They turn up in kernel and authentication logging, including event 4625.",
                "Note");
        }
    }

    private static ToolResult BuildSymbolResult(string query, IReadOnlyList<WindowsErrorEntry> matches)
    {
        if (matches.Count == 1)
        {
            WindowsErrorEntry only = matches[0];

            return TryParseCode(only.Code, out uint value)
                ? BuildNumericResult(value)
                : ToolResult.Build().Status(ResultStatus.Info, only.Symbol, only.Message).ToResult();
        }

        return ToolResult.Build()
            .Status(ResultStatus.Info, $"{matches.Count} codes match '{query}'", "Pick the one you meant.")
            .Table(
                "Matches",
                ["Code", "Symbol", "Family", "Message"],
                matches.Select(IReadOnlyList<TableCell> (m) =>
                [
                    new TableCell(m.Code, Monospace: true),
                    new TableCell(m.Symbol, Monospace: true),
                    new TableCell(m.Source),
                    new TableCell(m.Message),
                ]).ToList())
            .ToResult();
    }

    private static WindowsErrorEntry? Find(uint value) =>
        Dataset.Value.FirstOrDefault(e => TryParseCode(e.Code, out uint code) && code == value);

    private static IReadOnlyList<WindowsErrorEntry> SearchSymbols(string query) =>
        Dataset.Value
            .Where(e => e.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Symbol.Length)
            .Take(25)
            .ToList();

    /// <summary>Parse hex with or without the 0x prefix, or a signed or unsigned decimal.</summary>
    /// <param name="raw">The user's text.</param>
    /// <param name="value">The parsed 32-bit value.</param>
    public static bool TryParseCode(string raw, out uint value)
    {
        value = 0;
        string text = raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (text.Length == 0)
        {
            return false;
        }

        bool hexPrefixed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

        if (hexPrefixed)
        {
            text = text[2..];
        }

        if (hexPrefixed || text.EndsWith('h'))
        {
            return uint.TryParse(
                text.TrimEnd('h', 'H'),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out value);
        }

        // An unprefixed eight-character hex string is the shape every Windows error code
        // is written in, so 80070005 means 0x80070005 rather than eighty million. Shorter
        // all-digit input stays decimal, because "5" means five.
        if (text.Length == 8 && text.All(Uri.IsHexDigit))
        {
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        // Deployment tools report HRESULTs as negative signed integers.
        if (int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int signed))
        {
            value = unchecked((uint)signed);
            return true;
        }

        // A bare hex string such as C0000005 is unambiguous enough to accept.
        return text.All(Uri.IsHexDigit)
               && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static IReadOnlyList<WindowsErrorEntry> LoadDataset()
    {
        Assembly assembly = typeof(WindowsErrorCodeTool).Assembly;
        const string ResourceName = "AdminForge.Tools.Windows.ErrorCode.windows-error-codes.json";

        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The error-code dataset '{ResourceName}' is missing from the assembly. "
                + "Check that the JSON file is still marked as an EmbeddedResource in AdminForge.Tools.csproj.");

        return JsonSerializer.Deserialize<List<WindowsErrorEntry>>(stream, JsonOptions) ?? [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
