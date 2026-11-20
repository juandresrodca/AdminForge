using AdminForge.Core.Tools;
using AdminForge.Tools.Security.CertificateCsrDecoder;

namespace AdminForge.Tests.Tools;

/// <summary>
/// Metadata for the certificate csr decoder.
/// <para>
/// The behaviour of a client-side tool is tested in JavaScript — add cases to
/// tests/js/tools.test.mjs and run them with: node --test "tests/js/*.test.mjs"
/// </para>
/// </summary>
public sealed class CertificateCsrDecoderToolTests
{
    private static readonly CertificateCsrDecoderTool Tool = new();

    [Fact]
    public void Declares_itself_as_a_browser_tool()
    {
        Assert.Equal("certificate-csr-decoder", Tool.Id);
        Assert.Equal(ComputeMode.ClientSide, Tool.Compute);
    }
}
