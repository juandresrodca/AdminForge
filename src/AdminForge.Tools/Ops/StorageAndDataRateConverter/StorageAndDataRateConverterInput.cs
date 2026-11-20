using AdminForge.Core.Forms;

namespace AdminForge.Tools.Ops.StorageAndDataRateConverter;

/// <summary>Storage or rate value and optional transfer inputs.</summary>
public sealed class StorageAndDataRateConverterInput
{
    private const string StorageUnits = "B,KB,MB,GB,TB,PB,KiB,MiB,GiB,TiB,PiB";

    /// <summary>The amount to convert.</summary>
    [ToolField("Value", Placeholder = "4", Required = true, MaxLength = 64, Half = true)]
    public string Value { get; set; } = string.Empty;

    /// <summary>The unit of the input value.</summary>
    [ToolField("Unit", Kind = FieldKind.Select, Options = StorageUnits + ",bps,Kbps,Mbps,Gbps,KB/s,MB/s,GB/s",
        Required = true, Half = true)]
    public string Unit { get; set; } = "TB";

    /// <summary>Optional data size for a transfer-time estimate.</summary>
    [ToolField("Transfer size", Placeholder = "1", MaxLength = 64, Half = true,
        Help = "Optional with a rate. Enter a size to estimate how long it will take to transfer.")]
    public string? TransferSize { get; set; }

    /// <summary>The unit of the optional transfer size.</summary>
    [ToolField("Transfer size unit", Kind = FieldKind.Select, Options = StorageUnits, Half = true)]
    public string TransferSizeUnit { get; set; } = "GB";

    /// <summary>Optional duration for a transferable-size estimate.</summary>
    [ToolField("Transfer duration (seconds)", Placeholder = "60", MaxLength = 64,
        Help = "Optional with a rate. Enter a duration to estimate how much data you can transfer.")]
    public string? TransferSeconds { get; set; }
}
