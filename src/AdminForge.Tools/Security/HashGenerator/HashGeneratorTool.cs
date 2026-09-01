using AdminForge.Core.Forms;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Security.HashGenerator;

/// <summary>Input for the hash generator.</summary>
public sealed class HashGeneratorInput
{
    /// <summary>The text to hash.</summary>
    [ToolField("Text",
        Kind = FieldKind.TextArea,
        Rows = 6,
        Required = true,
        MaxLength = 100000,
        Placeholder = "Anything you want the digest of",
        Help = "Hashed as UTF-8 bytes.")]
    public string Text { get; set; } = string.Empty;

    /// <summary>A digest to compare the computed hashes against.</summary>
    [ToolField("Compare with",
        Placeholder = "paste a digest to verify against",
        MaxLength = 200,
        Help = "Optional. Any algorithm whose digest matches is highlighted.")]
    public string? Expected { get; set; }

    /// <summary>Whether to render digests in uppercase.</summary>
    [ToolField("Uppercase output")]
    public bool Uppercase { get; set; }
}

/// <summary>
/// Computes MD5, SHA-1, SHA-256, SHA-384 and SHA-512 digests in the browser, and
/// compares them against a digest you were given.
/// </summary>
public sealed class HashGeneratorTool : ITool, IClientTool<HashGeneratorInput>
{
    /// <inheritdoc />
    public string Id => "hash-generator";

    /// <inheritdoc />
    public string Name => "Hash generator";

    /// <inheritdoc />
    public string Description => "Compute MD5, SHA-1, SHA-256, SHA-384 and SHA-512 digests and verify a checksum";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Security;

    /// <inheritdoc />
    public string Icon => "hash";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ClientSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["md5", "sha1", "sha256", "sha512", "checksum", "digest", "fingerprint", "hash", "verify"];

    /// <inheritdoc />
    public string? Notes =>
        "MD5 and SHA-1 are included because checksums published years ago still use them, not because they are "
        + "safe — both are broken for anything that needs collision resistance. Use SHA-256 or better for new work. "
        + "Everything is computed in your browser, so hashing a secret is safe here.";
}
