using System.Net;
using AdminForge.Core.Configuration;
using AdminForge.Core.Net;
using AdminForge.Core.Results;
using AdminForge.Tools.Security.SecurityTxtValidator;
using Microsoft.Extensions.Options;

namespace AdminForge.Tests.Tools;

/// <summary>Behaviour of the security.txt validator.</summary>
public sealed class SecurityTxtValidatorToolTests
{
    [Fact]
    public async Task Accepts_a_complete_current_file_from_the_well_known_location()
    {
        var responses = new Dictionary<string, (HttpStatusCode Status, string Body)>
        {
            ["https://example.test/.well-known/security.txt"] =
                (HttpStatusCode.OK, "Contact: mailto:security@example.test\nExpires: 2099-01-01T00:00:00Z\nPolicy: https://example.test/policy\n"),
            ["https://example.test/security.txt"] = (HttpStatusCode.NotFound, string.Empty),
        };

        ToolResult result = await RunAsync(responses);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks, block => block is StatusBlock status && status.Status == ResultStatus.Ok);
        Assert.Contains(result.Blocks, block => block is KeyValueBlock values
            && values.Rows.Any(row => row.Label == "Contact" && row.Value.Contains("mailto:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Flags_an_expired_file_as_danger()
    {
        ToolResult result = await RunAsync(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["https://example.test/.well-known/security.txt"] =
                (HttpStatusCode.OK, "Contact: mailto:security@example.test\nExpires: 2000-01-01T00:00:00Z\n"),
            ["https://example.test/security.txt"] = (HttpStatusCode.NotFound, string.Empty),
        });

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks, block => block is StatusBlock status
            && status.Status == ResultStatus.Danger
            && status.Message.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Falls_back_to_the_legacy_location_and_reports_the_migration_finding()
    {
        ToolResult result = await RunAsync(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["https://example.test/.well-known/security.txt"] = (HttpStatusCode.NotFound, string.Empty),
            ["https://example.test/security.txt"] =
                (HttpStatusCode.OK, "Contact: mailto:security@example.test\nExpires: 2099-01-01T00:00:00Z\n"),
        });

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks, block => block is StatusBlock status
            && status.Status == ResultStatus.Warning
            && status.Message.Contains("legacy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Blocks, block => block is KeyValueBlock values
            && values.Rows.Any(row => row.Label == "Location"
                && row.Value.Contains("legacy", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Flags_missing_mandatory_fields()
    {
        ToolResult result = await RunAsync(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["https://example.test/.well-known/security.txt"] =
                (HttpStatusCode.OK, "Policy: https://example.test/security-policy\n"),
            ["https://example.test/security.txt"] = (HttpStatusCode.NotFound, string.Empty),
        });

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks, block => block is ListBlock list
            && list.Title == "Findings"
            && list.Items.Any(item => item.Contains("Contact", StringComparison.Ordinal)
                && item.Contains("Expires", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Strips_a_pgp_signature_before_parsing_fields()
    {
        const string signed = "-----BEGIN PGP SIGNED MESSAGE-----\n"
                              + "Hash: SHA256\n\n"
                              + "Contact: mailto:security@example.test\n"
                              + "Expires: 2099-01-01T00:00:00Z\n"
                              + "-----BEGIN PGP SIGNATURE-----\n"
                              + "not a field: should be ignored\n"
                              + "-----END PGP SIGNATURE-----\n";

        ToolResult result = await RunAsync(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["https://example.test/.well-known/security.txt"] = (HttpStatusCode.OK, signed),
            ["https://example.test/security.txt"] = (HttpStatusCode.NotFound, string.Empty),
        });

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks, block => block is KeyValueBlock values
            && values.Rows.Any(row => row.Label == "PGP signed" && row.Value.StartsWith("yes", StringComparison.Ordinal)));
        Assert.DoesNotContain(result.Blocks, block => block is ListBlock list
            && list.Title == "Unrecognised fields"
            && list.Items.Contains("not a field", StringComparer.Ordinal));
    }

    [Fact]
    public async Task Reports_a_missing_file_without_treating_it_as_a_transport_error()
    {
        ToolResult result = await RunAsync(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["https://example.test/.well-known/security.txt"] = (HttpStatusCode.NotFound, string.Empty),
            ["https://example.test/security.txt"] = (HttpStatusCode.NotFound, string.Empty),
        });

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains(result.Blocks, block => block is StatusBlock status
            && status.Status == ResultStatus.Danger
            && status.Message.Contains("No security.txt", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ToolResult> RunAsync(
        IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> responses)
    {
        var handler = new StubHandler(responses);
        var fetcher = new SafeHttpFetcher(
            new StubHttpClientFactory(handler),
            new StubTargetValidator(),
            new StubOptionsMonitor<AdminForgeOptions>(new AdminForgeOptions()));

        return await new SecurityTxtValidatorTool(fetcher)
            .ExecuteAsync(new SecurityTxtValidatorInput { Target = "example.test" }, CancellationToken.None);
    }

    private sealed class StubHandler(
        IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!responses.TryGetValue(request.RequestUri!.ToString(), out (HttpStatusCode Status, string Body) response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                    Content = new StringContent(string.Empty),
                });
            }

            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                RequestMessage = request,
                Content = new StringContent(response.Body),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubTargetValidator : IOutboundTargetValidator
    {
        public Task<TargetValidation> ValidateHostAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(TargetValidation.Allow(host, [IPAddress.Parse("93.184.216.34")]));

        public Task<(TargetValidation Validation, Uri? Uri)> ValidateUrlAsync(
            string url,
            CancellationToken cancellationToken)
        {
            Uri uri = new(url, UriKind.Absolute);
            return Task.FromResult<(TargetValidation, Uri?)>(
                (TargetValidation.Allow(uri.Host, [IPAddress.Parse("93.184.216.34")]), uri));
        }
    }

    private sealed class StubOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable OnChange(Action<T, string?> listener) => NoopDisposable.Instance;

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();

            public void Dispose() { }
        }
    }
}
