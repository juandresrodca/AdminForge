using AdminForge.Core.Forms;

namespace AdminForge.Tools.Security.TlsCertificate;

/// <summary>Input for the TLS certificate checker.</summary>
public sealed class TlsCertificateInput
{
    /// <summary>The host to connect to.</summary>
    [ToolField("Host",
        Placeholder = "example.com",
        Required = true,
        MaxLength = 253,
        Help = "A URL is accepted too — only the host is used.")]
    public string Host { get; set; } = string.Empty;

    /// <summary>The TCP port carrying TLS.</summary>
    [ToolField("Port",
        Kind = FieldKind.Number,
        Min = 1,
        Max = 65535,
        Half = true,
        Help = "443 for HTTPS, 465 or 993 for mail, 636 for LDAPS.")]
    public int Port { get; set; } = 443;

    /// <summary>Optional SNI override for hosts serving several certificates.</summary>
    [ToolField("SNI name",
        Placeholder = "same as host",
        Half = true,
        MaxLength = 253,
        Help = "Optional. Sends a different server name in the TLS handshake.")]
    public string? ServerName { get; set; }
}
