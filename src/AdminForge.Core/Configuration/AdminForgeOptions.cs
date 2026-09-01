namespace AdminForge.Core.Configuration;

/// <summary>
/// Deployment-level settings, bound from the <c>AdminForge</c> configuration section
/// (appsettings.json, or <c>AdminForge__*</c> environment variables in Docker).
/// </summary>
public sealed class AdminForgeOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AdminForge";

    /// <summary>
    /// Allow server-side tools to reach private, loopback and link-local addresses.
    /// <para>
    /// Off by default. A public AdminForge instance with this enabled is an open
    /// SSRF proxy into whatever network it runs on. Turn it on only for a homelab
    /// instance that is not reachable from the internet — which is exactly the case
    /// where pointing the certificate checker at 192.168.1.10 is the whole point.
    /// </para>
    /// </summary>
    public bool AllowPrivateTargets { get; set; }

    /// <summary>Wall-clock budget for a single server-side tool run, in seconds.</summary>
    public int ToolTimeoutSeconds { get; set; } = 15;

    /// <summary>Maximum bytes read from any outbound HTTP response.</summary>
    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Maximum redirects followed, each re-validated against the target rules.</summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>
    /// Extra hostnames or IP literals to refuse, on top of the built-in private-range
    /// rules. Matched case-insensitively against the requested host.
    /// </summary>
    public IList<string> BlockedHosts { get; set; } = [];

    /// <summary>Request throttling for server-side tools.</summary>
    public RateLimitOptions RateLimit { get; set; } = new();

    /// <summary>Optional banner shown on every page, e.g. to mark a public demo instance.</summary>
    public string? InstanceBanner { get; set; }
}

/// <summary>Throttling applied to server-side tool execution.</summary>
public sealed class RateLimitOptions
{
    /// <summary>Whether throttling is applied at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Permitted tool runs per client, per window.</summary>
    public int PermitLimit { get; set; } = 30;

    /// <summary>Window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>How many requests may wait for a permit before callers are rejected outright.</summary>
    public int QueueLimit { get; set; } = 4;
}
