using AdminForge.Core.Forms;

namespace AdminForge.Tools.Email.MailAuth;

/// <summary>Input for the mail authentication checker.</summary>
public sealed class MailAuthInput
{
    /// <summary>The domain whose mail authentication should be inspected.</summary>
    [ToolField("Domain",
        Placeholder = "contoso.com",
        Required = true,
        MaxLength = 253,
        Help = "The domain in the From address, not the mail server's hostname.")]
    public string Domain { get; set; } = string.Empty;

    /// <summary>Optional resolver to query instead of the system one.</summary>
    [ToolField("Resolver",
        Placeholder = "1.1.1.1",
        MaxLength = 45,
        Half = true,
        Help = "Optional. Worth setting when your local resolver truncates large TXT answers.")]
    public string? Resolver { get; set; }

    /// <summary>Extra DKIM selectors to probe, on top of the common ones.</summary>
    [ToolField("Extra DKIM selectors",
        Placeholder = "s1, mandrill, mailjet",
        MaxLength = 300,
        Half = true,
        Help = "Comma separated. Microsoft 365 (selector1/selector2) and Google (google) are always tried.")]
    public string? Selectors { get; set; }
}
