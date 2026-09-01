namespace AdminForge.Core.Tools;

/// <summary>
/// Top-level grouping shown in the tool gallery. Keep this list short — a category
/// that holds fewer than three tools is noise on the gallery page.
/// </summary>
public enum ToolCategory
{
    /// <summary>Addressing, name resolution, reachability, routing.</summary>
    Network,

    /// <summary>Certificates, hashing, tokens, credentials, hardening checks.</summary>
    Security,

    /// <summary>Windows-specific lookups: error codes, event IDs, registry, SIDs.</summary>
    Windows,

    /// <summary>Directory and access: Entra ID, Active Directory, LDAP, SAML, OIDC.</summary>
    Identity,

    /// <summary>Mail flow and deliverability: SPF, DKIM, DMARC, headers.</summary>
    Email,

    /// <summary>Encoding, serialisation and text transforms.</summary>
    Encoding,

    /// <summary>Day-to-day operations maths: capacity, scheduling, SLAs, units.</summary>
    Ops,
}
