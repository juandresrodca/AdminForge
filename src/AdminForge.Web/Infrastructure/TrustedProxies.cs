using System.Net;

namespace AdminForge.Web.Infrastructure;

/// <summary>
/// Turns the <c>AdminForge:TrustedProxies</c> setting into the proxy allow-list that
/// forwarded-header processing checks before believing <c>X-Forwarded-For</c>.
/// </summary>
public static class TrustedProxies
{
    /// <summary>
    /// Add each configured entry to the known proxies or networks. Loopback, which
    /// ASP.NET Core trusts by default, is kept.
    /// </summary>
    /// <param name="options">The forwarded-header options to populate.</param>
    /// <param name="entries">IP literals or CIDR ranges. Null or empty trusts loopback only.</param>
    /// <exception cref="InvalidOperationException">An entry is neither an address nor a CIDR range.</exception>
    public static void Apply(ForwardedHeadersOptions options, IEnumerable<string>? entries)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (string raw in entries ?? [])
        {
            string entry = raw.Trim();

            if (entry.Length == 0)
            {
                continue;
            }

            if (entry.Contains('/', StringComparison.Ordinal) && IPNetwork.TryParse(entry, out IPNetwork network))
            {
                options.KnownIPNetworks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out IPAddress? address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                // Failing at startup beats silently trusting nobody and throttling every
                // user of the instance as one client.
                throw new InvalidOperationException(
                    $"AdminForge:TrustedProxies entry '{entry}' is not an IP address or CIDR range.");
            }
        }
    }
}
