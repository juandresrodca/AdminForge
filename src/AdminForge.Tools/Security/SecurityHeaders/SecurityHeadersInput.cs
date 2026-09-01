using AdminForge.Core.Forms;

namespace AdminForge.Tools.Security.SecurityHeaders;

/// <summary>Input for the HTTP security headers checker.</summary>
public sealed class SecurityHeadersInput
{
    /// <summary>The URL to inspect.</summary>
    [ToolField("URL",
        Placeholder = "https://example.com",
        Required = true,
        MaxLength = 2048,
        Help = "https is assumed if you leave the scheme off. Redirects are followed and re-checked.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Whether to list every response header, not just the security-relevant ones.</summary>
    [ToolField("Show all response headers")]
    public bool ShowAllHeaders { get; set; }
}
