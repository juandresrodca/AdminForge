using AdminForge.Core.Forms;

namespace AdminForge.Tools.Network.IpRangeToCidr;

/// <summary>Input for the IP range to CIDR converter.</summary>
public sealed class IpRangeToCidrInput
{
    /// <summary>An inclusive IP address range or a list of CIDR blocks.</summary>
    [ToolField("IP range or CIDR blocks",
        Kind = FieldKind.TextArea,
        Placeholder = "192.0.2.10 - 192.0.2.87",
        Required = true,
        MaxLength = 16384,
        Help = "Enter a start and end address separated by a hyphen, or CIDR blocks separated by commas or whitespace.")]
    public string Addresses { get; set; } = string.Empty;
}
