using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AdminForge.Web.Infrastructure;

/// <summary>Reads the listening port a container platform hands the process in <c>PORT</c>.</summary>
public static class HostingPort
{
    /// <summary>
    /// Turn a <c>PORT</c> value into the URL Kestrel should bind. Anything that is not
    /// a port number is ignored, so the image's own default keeps applying.
    /// </summary>
    /// <param name="value">The raw environment variable, possibly null.</param>
    /// <param name="url">The URL to listen on, on every interface.</param>
    public static bool TryGetUrl(string? value, [NotNullWhen(true)] out string? url)
    {
        url = null;

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        url = $"http://*:{port.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }
}
