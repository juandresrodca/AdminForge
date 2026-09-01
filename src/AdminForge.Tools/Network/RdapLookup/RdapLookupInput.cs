using AdminForge.Core.Forms;

namespace AdminForge.Tools.Network.RdapLookup;

/// <summary>Input for the RDAP registration lookup.</summary>
public sealed class RdapLookupInput
{
    /// <summary>A domain name or an IP address.</summary>
    [ToolField("Domain or IP",
        Placeholder = "example.com",
        Required = true,
        MaxLength = 253,
        Help = "A domain returns its registration; an IP address returns the network allocation and its owner.")]
    public string Query { get; set; } = string.Empty;

    /// <summary>Whether to include the raw RDAP JSON in the output.</summary>
    [ToolField("Show raw RDAP response")]
    public bool ShowRaw { get; set; }
}
