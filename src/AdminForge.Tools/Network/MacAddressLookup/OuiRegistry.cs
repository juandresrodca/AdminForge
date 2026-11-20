using System.IO.Compression;
using System.Text.Json;

namespace AdminForge.Tools.Network.MacAddressLookup;

/// <summary>An IEEE registry assignment that covers an address.</summary>
/// <param name="Registry">MA-L, MA-M or MA-S.</param>
/// <param name="Prefix">The assigned prefix, upper-case hex without separators.</param>
/// <param name="Bits">The prefix length: 24, 28 or 36.</param>
/// <param name="Organisation">The registrant, as the IEEE publishes it.</param>
public sealed record OuiAssignment(string Registry, string Prefix, int Bits, string Organisation)
{
    /// <summary>The prefix written the way the IEEE writes it, e.g. <c>00-1A-2B</c> or <c>70-B3-D5-0A-1</c>.</summary>
    public string Display =>
        string.Join('-', Enumerable.Range(0, (Prefix.Length + 1) / 2)
            .Select(i => Prefix.Substring(i * 2, Math.Min(2, Prefix.Length - (i * 2)))));
}

/// <summary>
/// The IEEE MA-L, MA-M and MA-S registries, shipped inside the assembly so the lookup
/// works offline and never tells a third party which devices are on your network.
/// <para>
/// Regenerate the data with <c>tools/refresh-oui-registry.py</c>.
/// </para>
/// </summary>
public sealed class OuiRegistry
{
    private const string ResourceName = "AdminForge.Tools.Network.MacAddressLookup.oui-registry.json.gz";

    private static readonly Lazy<OuiRegistry> Embedded = new(Load);

    private readonly Dictionary<string, string> _large;
    private readonly Dictionary<string, string> _medium;
    private readonly Dictionary<string, string> _small;

    /// <summary>Build a registry from prefix-to-organisation maps, keyed by upper-case hex.</summary>
    /// <param name="large">MA-L: six hex digits.</param>
    /// <param name="medium">MA-M: seven hex digits.</param>
    /// <param name="small">MA-S: nine hex digits.</param>
    /// <param name="generated">When the data was produced.</param>
    public OuiRegistry(
        IReadOnlyDictionary<string, string> large,
        IReadOnlyDictionary<string, string> medium,
        IReadOnlyDictionary<string, string> small,
        string? generated = null)
    {
        _large = new Dictionary<string, string>(large, StringComparer.Ordinal);
        _medium = new Dictionary<string, string>(medium, StringComparer.Ordinal);
        _small = new Dictionary<string, string>(small, StringComparer.Ordinal);
        Generated = generated;
    }

    /// <summary>The registry that ships with AdminForge.</summary>
    public static OuiRegistry Shipped => Embedded.Value;

    /// <summary>The date the data was generated, as written by the refresh script.</summary>
    public string? Generated { get; }

    /// <summary>Total assignments across the three registries.</summary>
    public int Count => _large.Count + _medium.Count + _small.Count;

    /// <summary>
    /// Find the most specific assignment covering the address. MA-S and MA-M blocks
    /// are carved out of MA-L blocks the IEEE registered to itself, so the longest
    /// match is the real registrant.
    /// </summary>
    /// <param name="address">The address. Look up a group address with its I/G bit cleared.</param>
    public OuiAssignment? Find(MacAddress address)
    {
        if (_small.TryGetValue(address.Prefix(36), out string? small))
        {
            return new OuiAssignment("MA-S", address.Prefix(36), 36, small);
        }

        if (_medium.TryGetValue(address.Prefix(28), out string? medium))
        {
            return new OuiAssignment("MA-M", address.Prefix(28), 28, medium);
        }

        return _large.TryGetValue(address.Prefix(24), out string? large)
            ? new OuiAssignment("MA-L", address.Prefix(24), 24, large)
            : null;
    }

    private static OuiRegistry Load()
    {
        using Stream compressed = typeof(OuiRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The OUI registry '{ResourceName}' is missing from the assembly. Check that "
                + "AdminForge.Tools.csproj still embeds *.json.gz files.");
        using var json = new GZipStream(compressed, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement root = document.RootElement;

        return new OuiRegistry(
            Read(root, "MA-L"),
            Read(root, "MA-M"),
            Read(root, "MA-S"),
            root.TryGetProperty("generated", out JsonElement generated) ? generated.GetString() : null);
    }

    private static Dictionary<string, string> Read(JsonElement root, string registry)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (JsonProperty entry in root.GetProperty(registry).EnumerateObject())
        {
            entries[entry.Name] = entry.Value.GetString() ?? string.Empty;
        }

        return entries;
    }
}
