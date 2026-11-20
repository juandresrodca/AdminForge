using AdminForge.Core.Forms;

namespace AdminForge.Tools.Security.CertificateCsrDecoder;

/// <summary>Input for the certificate and CSR decoder.</summary>
public sealed class CertificateCsrDecoderInput
{
    [ToolField("Certificate or CSR",
        Kind = FieldKind.TextArea,
        Rows = 12,
        Required = true,
        MaxLength = 100000,
        Placeholder = "-----BEGIN CERTIFICATE-----\nMIIF...\n-----END CERTIFICATE-----",
        Help = "Paste a PEM-encoded X.509 certificate, certificate chain, or PKCS#10 CSR.")]
    public string Value { get; set; } = string.Empty;
}
