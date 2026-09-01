using System.Net;

namespace AdminForge.Core.Net;

/// <summary>The outcome of vetting a user-supplied network target.</summary>
/// <param name="IsAllowed">Whether the tool may contact the target.</param>
/// <param name="Host">The normalised hostname or IP literal.</param>
/// <param name="Addresses">Every address the host resolved to. Empty when validation failed early.</param>
/// <param name="Reason">User-facing explanation when the target was refused.</param>
public sealed record TargetValidation(
    bool IsAllowed,
    string Host,
    IReadOnlyList<IPAddress> Addresses,
    string? Reason)
{
    /// <summary>A permitted target.</summary>
    public static TargetValidation Allow(string host, IReadOnlyList<IPAddress> addresses) =>
        new(true, host, addresses, null);

    /// <summary>A refused target.</summary>
    public static TargetValidation Deny(string host, string reason) =>
        new(false, host, [], reason);
}

/// <summary>
/// Vets a hostname, IP literal or URL before any server-side tool contacts it.
/// </summary>
public interface IOutboundTargetValidator
{
    /// <summary>
    /// Resolve the host and check every address it maps to.
    /// <para>
    /// Resolution happens here, and the caller is expected to act on the returned
    /// addresses, because checking the name and then letting the HTTP stack resolve it
    /// again leaves a DNS-rebinding window between the two lookups.
    /// </para>
    /// </summary>
    /// <param name="host">Hostname or IP literal, without scheme or port.</param>
    /// <param name="cancellationToken">Cancellation for the DNS lookup.</param>
    Task<TargetValidation> ValidateHostAsync(string host, CancellationToken cancellationToken);

    /// <summary>
    /// Parse an absolute http or https URL and vet its host.
    /// </summary>
    /// <param name="url">The URL. A bare host is accepted and assumed to be https.</param>
    /// <param name="cancellationToken">Cancellation for the DNS lookup.</param>
    Task<(TargetValidation Validation, Uri? Uri)> ValidateUrlAsync(string url, CancellationToken cancellationToken);
}
