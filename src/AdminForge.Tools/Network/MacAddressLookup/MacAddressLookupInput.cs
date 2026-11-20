using AdminForge.Core.Forms;

namespace AdminForge.Tools.Network.MacAddressLookup;

/// <summary>Input for the MAC address and OUI vendor lookup.</summary>
public sealed class MacAddressLookupInput
{
    /// <summary>The MAC address, in any common notation.</summary>
    [ToolField("MAC address",
        Placeholder = "00:1A:2B:3C:4D:5E",
        Required = true,
        MaxLength = 64,
        Help = "Colons, hyphens, Cisco dots (001a.2b3c.4d5e) or bare hex. Case does not matter.")]
    public string Address { get; set; } = string.Empty;
}
