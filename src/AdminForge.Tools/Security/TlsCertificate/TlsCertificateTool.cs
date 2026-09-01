using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AdminForge.Core.Configuration;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;
using Microsoft.Extensions.Options;

namespace AdminForge.Tools.Security.TlsCertificate;

/// <summary>
/// Opens a TLS connection and reports what the server presented: expiry, names,
/// issuer, key strength, protocol version and the chain.
/// </summary>
/// <param name="validator">Vets the target before connecting.</param>
/// <param name="options">Deployment options, read for the connection timeout.</param>
public sealed class TlsCertificateTool(IOutboundTargetValidator validator, IOptionsMonitor<AdminForgeOptions> options)
    : ITool, IToolHandler<TlsCertificateInput>
{
    /// <summary>Below this many days remaining the result is flagged amber.</summary>
    private const int WarningDays = 21;

    /// <inheritdoc />
    public string Id => "tls-certificate-checker";

    /// <inheritdoc />
    public string Name => "TLS certificate checker";

    /// <inheritdoc />
    public string Description => "Check a certificate's expiry, names, issuer, key strength and negotiated protocol";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Security;

    /// <inheritdoc />
    public string Icon => "certificate";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ServerSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["ssl", "tls", "certificate", "expiry", "expires", "x509", "san", "chain", "https", "handshake", "pem"];

    /// <inheritdoc />
    public string? Notes =>
        "The certificate is read from a real handshake, so what you see is what that server actually serves for "
        + "the name you asked about — including the case where SNI selects a different certificate. Validation "
        + "errors are reported rather than thrown, so an expired or self-signed certificate is still inspectable.";

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(TlsCertificateInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        TargetValidation validation = await validator
            .ValidateHostAsync(input.Host, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.IsAllowed)
        {
            return ToolResult.Fail(validation.Reason!);
        }

        if (input.Port is < 1 or > 65535)
        {
            return ToolResult.Fail("The port must be between 1 and 65535.");
        }

        string sni = string.IsNullOrWhiteSpace(input.ServerName) ? validation.Host : input.ServerName.Trim();

        using var socket = new TcpClient();
        socket.ReceiveTimeout = options.CurrentValue.ToolTimeoutSeconds * 1000;

        try
        {
            await socket.ConnectAsync(validation.Addresses.ToArray(), input.Port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return ToolResult.Fail(
                $"Could not open a TCP connection to {validation.Host} on port {input.Port}. "
                + "Check the port is right and reachable from this instance.");
        }

        X509Certificate2? leaf = null;
        X509Chain? chain = null;
        SslPolicyErrors policyErrors = SslPolicyErrors.None;

        // The handshake is allowed to complete even when validation fails: inspecting a
        // broken certificate is the whole reason someone reaches for this tool.
        await using var tls = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false, (_, certificate, builtChain, errors) =>
        {
            leaf = certificate is null ? null : new X509Certificate2(certificate);
            chain = builtChain;
            policyErrors = errors;
            return true;
        });

        try
        {
            await tls.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = sni,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException)
        {
            return ToolResult.Fail(
                $"The TLS handshake with {validation.Host}:{input.Port} failed: {ex.Message.TrimEnd('.')}. "
                + "The port may not speak TLS directly — some services need STARTTLS.");
        }

        if (leaf is null)
        {
            return ToolResult.Fail($"{validation.Host} completed a handshake but presented no certificate.");
        }

        using (leaf)
        {
            return BuildResult(leaf, chain, policyErrors, tls, validation.Host, sni, input.Port);
        }
    }

    private static ToolResult BuildResult(
        X509Certificate2 leaf,
        X509Chain? chain,
        SslPolicyErrors policyErrors,
        SslStream tls,
        string host,
        string sni,
        int port)
    {
        DateTimeOffset notAfter = leaf.NotAfter.ToUniversalTime();
        DateTimeOffset notBefore = leaf.NotBefore.ToUniversalTime();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int daysLeft = (int)Math.Floor((notAfter - now).TotalDays);

        ResultBuilder result = ToolResult.Build();

        (ResultStatus status, string message) = daysLeft switch
        {
            < 0 => (ResultStatus.Danger, $"Expired {Math.Abs(daysLeft):N0} days ago"),
            0 => (ResultStatus.Danger, "Expires today"),
            <= WarningDays => (ResultStatus.Warning, $"Expires in {daysLeft:N0} days"),
            _ => (ResultStatus.Ok, $"Valid for another {daysLeft:N0} days"),
        };

        if (now < notBefore)
        {
            (status, message) = (ResultStatus.Danger, "Not valid yet");
        }

        result.Status(status, message, $"{host}:{port} · expires {notAfter:yyyy-MM-dd HH:mm} UTC");

        result.Status(
            policyErrors == SslPolicyErrors.None ? ResultStatus.Ok : ResultStatus.Danger,
            policyErrors == SslPolicyErrors.None
                ? "Chain validated against this machine's trust store"
                : $"Validation failed: {policyErrors}",
            policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
                ? $"The certificate does not cover '{sni}'."
                : null,
            "Trust");

        result.KeyValues("Certificate", kv =>
        {
            kv.Add("Subject", leaf.Subject, monospace: true);
            kv.Add("Issuer", leaf.Issuer, monospace: true);
            kv.Add("Serial number", leaf.SerialNumber, monospace: true);
            kv.Add("SHA-256 fingerprint", leaf.GetCertHashString(HashAlgorithmName.SHA256), monospace: true);
            kv.Add("Valid from", notBefore.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture), monospace: true);
            kv.Add("Valid until", notAfter.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
                status == ResultStatus.Ok ? ResultStatus.Neutral : status, monospace: true);
            kv.Add("Signature algorithm", DescribeSignature(leaf), SignatureStatus(leaf));
            kv.Add("Public key", DescribeKey(leaf), KeyStatus(leaf));
            kv.Add("Version", "v" + leaf.Version.ToString(CultureInfo.InvariantCulture));
        });

        result.KeyValues("Connection", kv =>
        {
            kv.Add("Protocol", tls.SslProtocol.ToString(), ProtocolStatus(tls.SslProtocol));
            kv.Add("Cipher suite", tls.NegotiatedCipherSuite.ToString(), monospace: true);
            kv.Add("SNI sent", sni, monospace: true);
        });

        IReadOnlyList<string> names = SubjectAlternativeNames(leaf);

        result.Table(
            $"Subject alternative names ({names.Count})",
            ["Name"],
            names.Select(IReadOnlyList<TableCell> (n) => [new TableCell(n, Monospace: true)]).ToList(),
            "This certificate carries no SAN extension. Modern clients require one.");

        if (chain is not null && chain.ChainElements.Count > 0)
        {
            result.Table(
                "Chain",
                ["#", "Subject", "Issuer", "Expires"],
                chain.ChainElements.Select(IReadOnlyList<TableCell> (element, index) =>
                [
                    new TableCell((index + 1).ToString(CultureInfo.InvariantCulture)),
                    new TableCell(ShortName(element.Certificate.Subject), Monospace: true),
                    new TableCell(ShortName(element.Certificate.Issuer), Monospace: true),
                    new TableCell(
                        element.Certificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Monospace: true),
                ]).ToList());
        }

        result.Code(
            leaf.ExportCertificatePem(),
            "pem",
            "Leaf certificate");

        return result.ToResult();
    }

    private static IReadOnlyList<string> SubjectAlternativeNames(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension san)
            {
                return [.. san.EnumerateDnsNames(), .. san.EnumerateIPAddresses().Select(ip => ip.ToString())];
            }
        }

        return [];
    }

    private static string ShortName(string distinguishedName)
    {
        foreach (string part in distinguishedName.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return part[3..];
            }
        }

        return distinguishedName;
    }

    private static string DescribeSignature(X509Certificate2 certificate) =>
        certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value ?? "unknown";

    private static ResultStatus SignatureStatus(X509Certificate2 certificate)
    {
        string name = (certificate.SignatureAlgorithm.FriendlyName ?? string.Empty).ToLowerInvariant();

        // SHA-1 and MD5 signatures have been rejected by browsers for years.
        return name.Contains("sha1", StringComparison.Ordinal) || name.Contains("md5", StringComparison.Ordinal)
            ? ResultStatus.Danger
            : ResultStatus.Neutral;
    }

    private static string DescribeKey(X509Certificate2 certificate)
    {
        using RSA? rsa = certificate.GetRSAPublicKey();

        if (rsa is not null)
        {
            return $"RSA {rsa.KeySize} bits";
        }

        using ECDsa? ecdsa = certificate.GetECDsaPublicKey();

        return ecdsa is not null
            ? $"ECDSA {ecdsa.KeySize} bits"
            : certificate.PublicKey.Oid.FriendlyName ?? "unknown";
    }

    private static ResultStatus KeyStatus(X509Certificate2 certificate)
    {
        using RSA? rsa = certificate.GetRSAPublicKey();

        if (rsa is null)
        {
            return ResultStatus.Neutral;
        }

        // 2048 bits is the floor the CA/Browser Forum baseline requirements set for RSA.
        return rsa.KeySize < 2048 ? ResultStatus.Danger : ResultStatus.Neutral;
    }

    private static ResultStatus ProtocolStatus(SslProtocols protocol) => protocol switch
    {
        SslProtocols.Tls13 or SslProtocols.Tls12 => ResultStatus.Ok,
        SslProtocols.None => ResultStatus.Neutral,
        _ => ResultStatus.Danger,
    };
}
