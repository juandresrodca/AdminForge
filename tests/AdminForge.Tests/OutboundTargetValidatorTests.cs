using AdminForge.Core.Configuration;
using AdminForge.Core.Net;
using Microsoft.Extensions.Options;

namespace AdminForge.Tests;

/// <summary>Operator-configured blocks, which must not be sidestepped by a subdomain or an address.</summary>
public sealed class OutboundTargetValidatorTests
{
    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("93.184.216.200")]
    [InlineData("2606:2800::1")]
    public async Task Blocks_an_address_named_directly_or_inside_a_blocked_range(string target)
    {
        var validator = new OutboundTargetValidator(Options(["93.184.216.34", "93.184.216.128/25", "2606:2800::/32"]));

        TargetValidation result = await validator.ValidateHostAsync(target, CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Contains("blocked", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allows_an_address_outside_every_blocked_entry()
    {
        var validator = new OutboundTargetValidator(Options(["93.184.216.128/25"]));

        TargetValidation result = await validator.ValidateHostAsync("93.184.216.34", CancellationToken.None);

        Assert.True(result.IsAllowed, result.Reason);
    }

    [Theory]
    [InlineData("internal.example", true)]
    [InlineData("api.internal.example", true)]
    [InlineData("API.Internal.Example.", true)]
    [InlineData("notinternal.example", false)]
    [InlineData("internal.example.org", false)]
    public async Task Blocks_a_blocked_name_and_its_subdomains(string host, bool blocked)
    {
        var validator = new OutboundTargetValidator(Options(["internal.example"]));

        TargetValidation result = await validator.ValidateHostAsync(host, CancellationToken.None);

        // Unblocked names here do not resolve, which is a different refusal.
        Assert.Equal(blocked, result.Reason?.Contains("blocked", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static IOptionsMonitor<AdminForgeOptions> Options(IList<string> blocked) =>
        new FixedOptions(new AdminForgeOptions { BlockedHosts = blocked });

    private sealed class FixedOptions(AdminForgeOptions value) : IOptionsMonitor<AdminForgeOptions>
    {
        public AdminForgeOptions CurrentValue => value;

        public AdminForgeOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AdminForgeOptions, string?> listener) => null;
    }
}
