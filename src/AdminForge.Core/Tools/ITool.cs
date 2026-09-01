namespace AdminForge.Core.Tools;

/// <summary>
/// The one contract every AdminForge tool implements.
/// <para>
/// This is metadata only — it describes the tool to the gallery, the search box and
/// the router. Tools that need the server to do work also implement
/// <see cref="IToolHandler{TInput}"/>.
/// </para>
/// <para>
/// Implementations are discovered automatically at startup. You never register a
/// tool by hand and you never edit a core file to add one.
/// </para>
/// </summary>
public interface ITool
{
    /// <summary>
    /// Stable, unique, kebab-case identifier. This becomes the URL
    /// (<c>/tools/subnet-calculator</c>), so treat it as public API: renaming one
    /// breaks every bookmark and shared link pointing at it.
    /// </summary>
    string Id { get; }

    /// <summary>Human-readable name shown on the gallery card and page heading.</summary>
    string Name { get; }

    /// <summary>
    /// One sentence, sentence case, no trailing period. Shown on the gallery card
    /// and in search results — write what the tool does, not what it is.
    /// </summary>
    string Description { get; }

    /// <summary>Gallery grouping.</summary>
    ToolCategory Category { get; }

    /// <summary>
    /// Icon id from <c>wwwroot/icons/sprite.svg</c>. Reuse an existing one where it
    /// fits; adding a new icon means adding a <c>&lt;symbol&gt;</c> to that sprite.
    /// </summary>
    string Icon { get; }

    /// <summary>Where the work happens. Drives the badge shown on the tool page.</summary>
    ComputeMode Compute { get; }

    /// <summary>
    /// Extra search terms — abbreviations, synonyms and the names people actually
    /// type. A subnet calculator should match "cidr", "netmask", "vlsm" and
    /// "supernet". These cost nothing and are the difference between a tool being
    /// found and being invisible.
    /// </summary>
    IReadOnlyList<string> Keywords => [];

    /// <summary>
    /// Optional longer-form help rendered under the tool's form, as Markdown-free
    /// plain text. Use it for caveats, RFC references and "why the answer differs
    /// from tool X" notes.
    /// </summary>
    string? Notes => null;
}
