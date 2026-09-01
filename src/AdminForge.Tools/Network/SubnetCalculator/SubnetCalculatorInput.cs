using AdminForge.Core.Forms;

namespace AdminForge.Tools.Network.SubnetCalculator;

/// <summary>Input for the subnet calculator.</summary>
public sealed class SubnetCalculatorInput
{
    /// <summary>The network in CIDR form, or an address with a dotted-decimal mask.</summary>
    [ToolField("Network",
        Placeholder = "10.20.0.0/22",
        Required = true,
        MaxLength = 128,
        Help = "IPv4 or IPv6. Accepts 10.20.0.0/22, 10.20.0.5/22 or 10.20.0.0 255.255.252.0.")]
    public string Network { get; set; } = string.Empty;

    /// <summary>Optional new prefix length used to split the network into equal subnets.</summary>
    [ToolField("Split into /",
        Kind = FieldKind.Number,
        Placeholder = "24",
        Min = 1,
        Max = 128,
        Half = true,
        Help = "Optional. Divides the network into equal subnets of this prefix length.")]
    public int? SplitInto { get; set; }

    /// <summary>How many of the split subnets to list.</summary>
    [ToolField("Show first",
        Kind = FieldKind.Number,
        Min = 1,
        Max = 256,
        Half = true,
        Help = "How many subnets to list when splitting.")]
    public int SplitLimit { get; set; } = 16;
}
