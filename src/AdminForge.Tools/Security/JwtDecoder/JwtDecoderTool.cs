using AdminForge.Core.Forms;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Security.JwtDecoder;

/// <summary>Input for the JWT decoder.</summary>
public sealed class JwtDecoderInput
{
    /// <summary>The token to decode.</summary>
    [ToolField("Token",
        Kind = FieldKind.TextArea,
        Rows = 7,
        Required = true,
        MaxLength = 20000,
        Placeholder = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0In0.signature",
        Help = "Paste the whole token, including the signature.")]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Decodes a JSON Web Token's header and payload and interprets the standard claims.
/// <para>
/// Deliberately client-side: a JWT is a bearer credential, and pasting one into a
/// website that posts it to a server hands over whatever it authorises. This tool
/// makes no request at all, which is the whole reason it belongs in a self-hosted
/// toolbox.
/// </para>
/// </summary>
public sealed class JwtDecoderTool : ITool, IClientTool<JwtDecoderInput>
{
    /// <inheritdoc />
    public string Id => "jwt-decoder";

    /// <inheritdoc />
    public string Name => "JWT decoder";

    /// <inheritdoc />
    public string Description => "Decode a JSON Web Token's header and claims without sending it anywhere";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Security;

    /// <inheritdoc />
    public string Icon => "token";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ClientSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["jwt", "json web token", "bearer", "claims", "oauth", "oidc", "access token", "id token", "entra", "azure ad"];

    /// <inheritdoc />
    public string? Notes =>
        "Decoding is not verifying. This tool reads what the token says; it does not check the signature, so "
        + "never trust a decoded claim as proof of anything. Expiry and not-before times are compared against "
        + "your browser's clock, so a wrong local clock will make a valid token look expired.";
}
