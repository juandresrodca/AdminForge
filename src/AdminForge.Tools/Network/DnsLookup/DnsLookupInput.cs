using AdminForge.Core.Forms;

namespace AdminForge.Tools.Network.DnsLookup;

/// <summary>The record types the lookup tool offers.</summary>
public enum DnsRecordKind
{
    /// <summary>IPv4 address records.</summary>
    A,

    /// <summary>IPv6 address records.</summary>
    AAAA,

    /// <summary>Mail exchanger records.</summary>
    MX,

    /// <summary>Text records, including SPF and verification tokens.</summary>
    TXT,

    /// <summary>Authoritative name servers.</summary>
    NS,

    /// <summary>Canonical name aliases.</summary>
    CNAME,

    /// <summary>Start of authority.</summary>
    SOA,

    /// <summary>Certification authority authorisation.</summary>
    CAA,

    /// <summary>Service location records.</summary>
    SRV,

    /// <summary>Reverse pointer records.</summary>
    PTR,
}

/// <summary>Input for the DNS lookup tool.</summary>
public sealed class DnsLookupInput
{
    /// <summary>The name to query.</summary>
    [ToolField("Domain or host",
        Placeholder = "example.com",
        Required = true,
        MaxLength = 253)]
    public string Domain { get; set; } = string.Empty;

    /// <summary>Which record type to ask for.</summary>
    [ToolField("Record type", Kind = FieldKind.Select, Half = true)]
    public DnsRecordKind RecordType { get; set; } = DnsRecordKind.A;

    /// <summary>Optional resolver to query instead of the system one.</summary>
    [ToolField("Resolver",
        Placeholder = "1.1.1.1",
        Half = true,
        MaxLength = 45,
        Help = "Optional. Leave empty to use the resolver this instance is configured with.")]
    public string? Resolver { get; set; }
}
