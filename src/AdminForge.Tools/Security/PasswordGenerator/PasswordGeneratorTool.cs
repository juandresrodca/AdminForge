using AdminForge.Core.Forms;
using AdminForge.Core.Tools;

namespace AdminForge.Tools.Security.PasswordGenerator;

/// <summary>What kind of secret to generate.</summary>
public enum SecretStyle
{
    /// <summary>A random character string.</summary>
    Password,

    /// <summary>A sequence of random words, in the style of the XKCD 936 comic.</summary>
    Passphrase,
}

/// <summary>Input for the password generator.</summary>
public sealed class PasswordGeneratorInput
{
    /// <summary>Password or passphrase.</summary>
    [ToolField("Style", Kind = FieldKind.Select, Half = true)]
    public SecretStyle Style { get; set; } = SecretStyle.Password;

    /// <summary>How many secrets to produce.</summary>
    [ToolField("How many", Kind = FieldKind.Number, Min = 1, Max = 50, Half = true)]
    public int Count { get; set; } = 5;

    /// <summary>Password length in characters.</summary>
    [ToolField("Length",
        Kind = FieldKind.Number,
        Min = 4,
        Max = 256,
        Half = true,
        Help = "Characters, for the password style.")]
    public int Length { get; set; } = 20;

    /// <summary>Number of words in a passphrase.</summary>
    [ToolField("Words",
        Kind = FieldKind.Number,
        Min = 3,
        Max = 12,
        Half = true,
        Help = "Words, for the passphrase style. Each word from the 256-word list adds 8 bits.")]
    public int Words { get; set; } = 8;

    /// <summary>Include A–Z.</summary>
    [ToolField("Uppercase A-Z")]
    public bool Uppercase { get; set; } = true;

    /// <summary>Include a–z.</summary>
    [ToolField("Lowercase a-z")]
    public bool Lowercase { get; set; } = true;

    /// <summary>Include 0–9.</summary>
    [ToolField("Digits 0-9")]
    public bool Digits { get; set; } = true;

    /// <summary>Include punctuation.</summary>
    [ToolField("Symbols")]
    public bool Symbols { get; set; } = true;

    /// <summary>Drop characters that are easy to misread.</summary>
    [ToolField("Exclude look-alike characters",
        Help = "Removes I, l, 1, O and 0 — worth it when someone will read the password aloud or type it from a screen.")]
    public bool ExcludeAmbiguous { get; set; }
}

/// <summary>
/// Generates passwords and passphrases in the browser and scores how much guessing
/// each one would actually take.
/// <para>
/// Client-side by necessity: a password generated on someone else's server is not a
/// secret. Randomness comes from the browser's cryptographic generator, never
/// <c>Math.random</c>.
/// </para>
/// </summary>
public sealed class PasswordGeneratorTool : ITool, IClientTool<PasswordGeneratorInput>
{
    /// <inheritdoc />
    public string Id => "password-generator";

    /// <inheritdoc />
    public string Name => "Password generator";

    /// <inheritdoc />
    public string Description => "Generate passwords or passphrases in your browser and see their real entropy";

    /// <inheritdoc />
    public ToolCategory Category => ToolCategory.Security;

    /// <inheritdoc />
    public string Icon => "key";

    /// <inheritdoc />
    public ComputeMode Compute => ComputeMode.ClientSide;

    /// <inheritdoc />
    public IReadOnlyList<string> Keywords =>
        ["password", "passphrase", "entropy", "random", "generator", "diceware", "xkcd", "secret", "credential"];

    /// <inheritdoc />
    public string? Notes =>
        "Entropy is calculated from the generator's own choices — the size of the alphabet and the length — which "
        + "is the honest measure for a random secret. It says nothing about a password a human invented, where the "
        + "predictability of the pattern matters far more than the character count.";
}
