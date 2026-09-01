using System.Net;
using System.Net.Sockets;
using AdminForge.Core.Configuration;
using Microsoft.Extensions.Options;

namespace AdminForge.Core.Net;

/// <summary>
/// Default <see cref="IOutboundTargetValidator"/>. Refuses private and reserved
/// address space unless the operator has explicitly opted in with
/// <see cref="AdminForgeOptions.AllowPrivateTargets"/>.
/// </summary>
/// <param name="options">Live deployment options.</param>
public sealed class OutboundTargetValidator(IOptionsMonitor<AdminForgeOptions> options) : IOutboundTargetValidator
{
    private static readonly char[] HostTrimChars = ['.', ' ', '\t'];

    /// <inheritdoc />
    public async Task<TargetValidation> ValidateHostAsync(string host, CancellationToken cancellationToken)
    {
        AdminForgeOptions settings = options.CurrentValue;

        if (string.IsNullOrWhiteSpace(host))
        {
            return TargetValidation.Deny(string.Empty, "No target was supplied.");
        }

        string normalised = Normalise(host);

        if (normalised.Length == 0)
        {
            return TargetValidation.Deny(host, "That does not look like a hostname or IP address.");
        }

        if (settings.BlockedHosts.Any(b => string.Equals(b, normalised, StringComparison.OrdinalIgnoreCase)))
        {
            return TargetValidation.Deny(normalised, $"{normalised} is blocked by this instance's configuration.");
        }

        // "localhost" resolves differently across platforms and container runtimes, so it is
        // refused by name and the answer does not depend on the host's resolver.
        if (!settings.AllowPrivateTargets && IsLocalhostName(normalised))
        {
            return TargetValidation.Deny(normalised, PrivateDenialMessage(normalised));
        }

        IReadOnlyList<IPAddress> addresses;

        if (IPAddress.TryParse(normalised, out IPAddress? literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(normalised, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                return TargetValidation.Deny(normalised, $"{normalised} could not be resolved.");
            }

            if (addresses.Count == 0)
            {
                return TargetValidation.Deny(normalised, $"{normalised} did not resolve to any address.");
            }
        }

        if (settings.AllowPrivateTargets)
        {
            return TargetValidation.Allow(normalised, addresses);
        }

        // Every resolved address must be public. A name returning one public and one private
        // address is still a rebinding vector, so the whole target is refused.
        return addresses.Any(IpAddressRules.IsPrivateOrReserved)
            ? TargetValidation.Deny(normalised, PrivateDenialMessage(normalised))
            : TargetValidation.Allow(normalised, addresses);
    }

    /// <inheritdoc />
    public async Task<(TargetValidation Validation, Uri? Uri)> ValidateUrlAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (TargetValidation.Deny(string.Empty, "No URL was supplied."), null);
        }

        string candidate = url.Trim();

        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            return (TargetValidation.Deny(url, "That is not a valid URL."), null);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return (TargetValidation.Deny(uri.Host, $"Only http and https URLs are supported, not {uri.Scheme}."), null);
        }

        TargetValidation validation = await ValidateHostAsync(uri.Host, cancellationToken).ConfigureAwait(false);
        return (validation, validation.IsAllowed ? uri : null);
    }

    private static bool IsLocalhostName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string host)
    {
        string value = host.Trim().Trim(HostTrimChars);

        // Accept a pasted URL or host:port where a bare host was expected.
        if (value.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return parsed.Host;
        }

        // Bracketed IPv6 literal, optionally with a port.
        if (value.StartsWith('[') && value.Contains(']'))
        {
            return value[1..value.IndexOf(']')];
        }

        int colon = value.IndexOf(':');

        // One colon means host:port. Several mean a bare IPv6 literal, which stays whole.
        if (colon > 0 && value.IndexOf(':', colon + 1) < 0)
        {
            value = value[..colon];
        }

        int slash = value.IndexOf('/');

        if (slash > 0)
        {
            value = value[..slash];
        }

        return value;
    }

    private static string PrivateDenialMessage(string host) =>
        $"{host} resolves to a private, loopback or reserved address. This instance only contacts "
        + "public targets. A self-hosted instance can allow internal targets by setting "
        + "AdminForge:AllowPrivateTargets to true.";
}
