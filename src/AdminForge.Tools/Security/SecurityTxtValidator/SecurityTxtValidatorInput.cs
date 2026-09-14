using AdminForge.Core.Forms;

namespace AdminForge.Tools.Security.SecurityTxtValidator;

/// <summary>Input for the security.txt validator.</summary>
public sealed class SecurityTxtValidatorInput
{
    /// <summary>The domain or URL whose security.txt should be checked.</summary>
    [ToolField("Domain or URL",
        Placeholder = "example.com",
        Required = true,
        MaxLength = 2048,
        Help = "The scheme is optional. Both the RFC 9116 and legacy root locations are checked.")]
    public string Target { get; set; } = string.Empty;
}
