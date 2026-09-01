namespace AdminForge.Web.Infrastructure;

/// <summary>
/// Applies the response headers a self-hosted security tool is expected to set on
/// itself. AdminForge ships a security-headers checker, so failing its own check
/// would be a poor look.
/// <para>
/// The content security policy is deliberately strict: no inline script, no inline
/// style, no third-party origins. Every asset AdminForge serves is local, which also
/// means the app works in an air-gapped network and makes no third-party requests.
/// </para>
/// </summary>
/// <param name="next">The next middleware.</param>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self'; "
        + "style-src 'self'; "
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'; "
        + "base-uri 'self'; "
        + "object-src 'none'";

    /// <summary>Adds the headers, then continues the pipeline.</summary>
    /// <param name="context">The request context.</param>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IHeaderDictionary headers = context.Response.Headers;

        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // Server banners tell an attacker what to target and help nobody else.
        headers.Remove("Server");
        headers.Remove("X-Powered-By");

        return next(context);
    }
}

/// <summary>Named rate-limiting policies.</summary>
public static class RateLimitPolicies
{
    /// <summary>Applied to server-side tool execution.</summary>
    public const string Tools = "tools";
}
