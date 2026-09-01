namespace AdminForge.Core.Tools;

/// <summary>
/// Where a tool actually does its work. This is surfaced to the user as a badge on
/// every tool page, because "does my secret leave this machine?" is the first
/// question a security-minded admin asks.
/// </summary>
public enum ComputeMode
{
    /// <summary>
    /// Runs entirely in the browser. Input never reaches the server — no request is
    /// made at all. Use this for anything that touches secrets (tokens, passwords,
    /// private keys) and for pure transforms that need no network access.
    /// </summary>
    ClientSide,

    /// <summary>
    /// Input is posted to the AdminForge server, which does the work and returns a
    /// result. Required when the tool needs outbound network access or a dataset too
    /// large to ship to the browser.
    /// </summary>
    ServerSide,
}
