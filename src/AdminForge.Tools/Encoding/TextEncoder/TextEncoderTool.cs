using AdminForge.Core.Forms;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Encoding.TextEncoder;

/// <summary>Which encoding to apply.</summary>
public enum EncodingFormat
{
    /// <summary>Standard base64, with padding.</summary>
    Base64,

    /// <summary>URL-safe base64: minus and underscore, no padding.</summary>
    Base64Url,

    /// <summary>Percent-encoding for URL components.</summary>
    Url,

    /// <summary>Lowercase hexadecimal of the UTF-8 bytes.</summary>
    Hex,

    /// <summary>HTML entity escaping for the five reserved characters.</summary>
    HtmlEntities,
}

/// <summary>Which direction to run.</summary>
public enum EncodingDirection
{
    /// <summary>Plain text to encoded form.</summary>
    Encode,

    /// <summary>Encoded form back to plain text.</summary>
    Decode,
}

/// <summary>Input for the encoder and decoder.</summary>
public sealed class TextEncoderInput
{
    /// <summary>The text to transform.</summary>
    [ToolField("Input",
        Kind = FieldKind.TextArea,
        Rows = 7,
        Required = true,
        MaxLength = 200000,
        Placeholder = "Paste text or an encoded value")]
    public string Input { get; set; } = string.Empty;

    /// <summary>The encoding to use.</summary>
    [ToolField("Format", Kind = FieldKind.Select, Half = true)]
    public EncodingFormat Format { get; set; } = EncodingFormat.Base64;

    /// <summary>Whether to encode or decode.</summary>
    [ToolField("Direction", Kind = FieldKind.Select, Half = true)]
    public EncodingDirection Direction { get; set; } = EncodingDirection.Encode;
}

/// <summary>
/// Encodes and decodes base64, base64url, percent-encoding, hex and HTML entities.
/// <para>
/// Client-side, because base64 is what people paste secrets into: a PowerShell
/// <c>-EncodedCommand</c>, a Kubernetes secret, a basic-auth header. None of it
/// leaves the browser.
/// </para>
/// </summary>
public sealed class TextEncoderTool : ITool, IClientTool<TextEncoderInput>
{
    /// <inheritdoc />
    public string Id => "text-encoder";

    /// <inheritdoc />
    public string Name => "Encoder and decoder";

    /// <inheritdoc />
    public string Description => "Convert text to and from base64, base64url, percent-encoding, hex and HTML entities";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Encoding;

    /// <inheritdoc />
    public string Icon => "code";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ClientSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["base64", "base64url", "url encode", "percent encoding", "hex", "html entities", "escape", "decode", "encode"];

    /// <inheritdoc />
    public string? Notes =>
        "Base64 is an encoding, not encryption — anything encoded here is trivially readable by anyone who has it. "
        + "Kubernetes secrets and basic-auth headers are base64, which is exactly why they need to be treated as "
        + "plaintext credentials.";
}
