using AdminForge.Core.Tools;

namespace AdminForge.Tools.Security.CertificateCsrDecoder;

/// <summary>
/// Decodes PEM-encoded X.509 certificates and PKCS#10 certificate
/// signing requests entirely in the browser.
/// </summary>
public sealed class CertificateCsrDecoderTool
    : ITool, IClientTool<CertificateCsrDecoderInput>
{
    public string Id => "certificate-csr-decoder";

    public string Name => "Certificate / CSR decoder";

    public string Description =>
        "Decode certificates and CSRs without contacting a server";

    public ToolCategory Category => ToolCategory.Security;

    public string Icon => "tool";

    public ComputeMode Compute => ComputeMode.ClientSide;

    public IReadOnlyList<string> Keywords =>
        ["ssl", "tls", "certificate", "csr", "pem", "x509",
         "subject alternative name", "san", "openssl"];

    public string? Notes =>
        "Certificate and CSR contents are processed locally in your browser. "
        + "Decoding does not establish certificate trust or verify a CA chain.";
}
