using System.Diagnostics;
using System.Net;
using System.Text;
using AdminForge.Core.Configuration;
using Microsoft.Extensions.Options;

namespace AdminForge.Core.Net;

/// <summary>One hop in a redirect chain.</summary>
/// <param name="Url">The URL requested at this hop.</param>
/// <param name="StatusCode">The status returned.</param>
/// <param name="Location">The location header that moved us on, if any.</param>
public readonly record struct RedirectHop(string Url, int StatusCode, string? Location);

/// <summary>A capped, fully vetted HTTP response.</summary>
/// <param name="StatusCode">Final status code.</param>
/// <param name="ReasonPhrase">Final status text.</param>
/// <param name="FinalUrl">The URL that actually produced the response.</param>
/// <param name="Headers">Response headers, joined where a header repeats.</param>
/// <param name="Body">Response body, truncated to the configured cap.</param>
/// <param name="BodyTruncated">Whether the body hit the cap.</param>
/// <param name="Hops">Every redirect followed to get here.</param>
/// <param name="Elapsed">Total wall-clock time.</param>
public sealed record SafeHttpResponse(
    int StatusCode,
    string? ReasonPhrase,
    string FinalUrl,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    bool BodyTruncated,
    IReadOnlyList<RedirectHop> Hops,
    TimeSpan Elapsed);

/// <summary>
/// Fetches a user-supplied URL with every guard a public instance needs: the target
/// is vetted before the request, every redirect is re-vetted, the socket refuses to
/// connect to a private address even if DNS changes mid-flight, and the body is
/// capped.
/// <para>
/// Network tools depend on this rather than a bare <see cref="HttpClient"/>, so the
/// protection is inherited rather than reimplemented per tool.
/// </para>
/// </summary>
public sealed class SafeHttpFetcher(
    IHttpClientFactory httpClientFactory,
    IOutboundTargetValidator validator,
    IOptionsMonitor<AdminForgeOptions> options)
{
    /// <summary>Name of the hardened <see cref="HttpClient"/> registration.</summary>
    public const string ClientName = "AdminForge.Safe";

    /// <summary>
    /// Fetch a URL, following redirects manually so each hop is validated.
    /// </summary>
    /// <param name="url">The URL to fetch. A bare host is assumed to be https.</param>
    /// <param name="method">HTTP method. Defaults to GET.</param>
    /// <param name="cancellationToken">Cancellation for the whole operation.</param>
    /// <returns>The response, or a message explaining why the fetch was refused.</returns>
    public async Task<(SafeHttpResponse? Response, string? Error)> FetchAsync(
        string url,
        HttpMethod? method = null,
        CancellationToken cancellationToken = default)
    {
        AdminForgeOptions settings = options.CurrentValue;
        var stopwatch = Stopwatch.StartNew();

        (TargetValidation validation, Uri? uri) = await validator
            .ValidateUrlAsync(url, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.IsAllowed || uri is null)
        {
            return (null, validation.Reason ?? "That target is not allowed.");
        }

        HttpClient client = httpClientFactory.CreateClient(ClientName);
        var hops = new List<RedirectHop>();
        Uri current = uri;

        for (int redirect = 0; redirect <= settings.MaxRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, current);
            HttpResponseMessage response;

            try
            {
                response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (null, $"{current.Host} did not respond within {settings.ToolTimeoutSeconds} seconds.");
            }
            catch (HttpRequestException ex)
            {
                return (null, $"Could not reach {current.Host}: {ex.Message}");
            }

            using (response)
            {
                string? location = response.Headers.Location?.ToString();

                if (IsRedirect(response.StatusCode) && location is not null)
                {
                    hops.Add(new RedirectHop(current.ToString(), (int)response.StatusCode, location));

                    if (!Uri.TryCreate(current, location, out Uri? next))
                    {
                        return (null, $"{current.Host} returned an unusable redirect to '{location}'.");
                    }

                    // Re-vet every hop: a permitted public URL is allowed to redirect
                    // to an internal one, and that is the classic SSRF pivot.
                    (TargetValidation hopValidation, Uri? hopUri) = await validator
                        .ValidateUrlAsync(next.ToString(), cancellationToken)
                        .ConfigureAwait(false);

                    if (!hopValidation.IsAllowed || hopUri is null)
                    {
                        return (null, $"Redirect to {next.Host} was refused. {hopValidation.Reason}");
                    }

                    current = hopUri;
                    continue;
                }

                (string body, bool truncated) = await ReadCappedAsync(
                    response,
                    settings.MaxResponseBytes,
                    cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                return (new SafeHttpResponse(
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    current.ToString(),
                    CollectHeaders(response),
                    body,
                    truncated,
                    hops,
                    stopwatch.Elapsed), null);
            }
        }

        return (null, $"Gave up after {settings.MaxRedirects} redirects.");
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static Dictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, IEnumerable<string> values) in response.Headers)
        {
            headers[key] = string.Join(", ", values);
        }

        foreach ((string key, IEnumerable<string> values) in response.Content.Headers)
        {
            headers[key] = string.Join(", ", values);
        }

        return headers;
    }

    private static async Task<(string Body, bool Truncated)> ReadCappedAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        byte[] buffer = new byte[maxBytes];
        int total = 0;

        while (total < maxBytes)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(total, maxBytes - total), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        // The stream still having data means we stopped at the cap rather than the end.
        bool truncated = total == maxBytes && stream.ReadByte() != -1;

        return (Encoding.UTF8.GetString(buffer, 0, total), truncated);
    }
}
