using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AdminForge.Tools.Security.TlsCertificate;

namespace AdminForge.Tests.Tools;

/// <summary>The certificate chain has to survive SslStream disposing it after validation.</summary>
public sealed class TlsCertificateToolTests
{
    [Fact]
    public void A_snapshot_of_the_chain_outlives_the_chain()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=snapshot.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        DateTimeOffset notAfter = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
        using X509Certificate2 certificate = request.CreateSelfSigned(notAfter.AddYears(-1), notAfter);

        IReadOnlyList<ChainEntry> snapshot;

        // This is what SslStream does to the chain it passes the validation callback.
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(certificate);
            snapshot = TlsCertificateTool.SnapshotChain(chain);
        }

        ChainEntry entry = Assert.Single(snapshot);
        Assert.Equal("CN=snapshot.test", entry.Subject);
        Assert.Equal("CN=snapshot.test", entry.Issuer);
        Assert.Equal(notAfter.UtcDateTime, entry.NotAfter);
    }

    [Fact]
    public void No_chain_gives_an_empty_snapshot() =>
        Assert.Empty(TlsCertificateTool.SnapshotChain(null));
}
