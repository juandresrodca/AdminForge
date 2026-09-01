using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AdminForge.Core.Configuration;
using AdminForge.Core.Net;
using AdminForge.Core.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdminForge.Core;

/// <summary>Wires AdminForge into an ASP.NET Core application.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers options, the hardened HTTP stack, and every tool found in the given
    /// assemblies.
    /// <para>
    /// Tools are discovered by reflection, so this call never needs editing when a
    /// tool is added — which is the entire point of the architecture.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root, read for the <c>AdminForge</c> section.</param>
    /// <param name="toolAssemblies">Assemblies to scan for <see cref="Tools.ITool"/> implementations.</param>
    public static IServiceCollection AddAdminForge(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] toolAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<AdminForgeOptions>()
            .Bind(configuration.GetSection(AdminForgeOptions.SectionName))
            .Validate(o => o.ToolTimeoutSeconds is > 0 and <= 120,
                "AdminForge:ToolTimeoutSeconds must be between 1 and 120.")
            .Validate(o => o.MaxResponseBytes is >= 1024 and <= 64 * 1024 * 1024,
                "AdminForge:MaxResponseBytes must be between 1 KiB and 64 MiB.")
            .Validate(o => o.MaxRedirects is >= 0 and <= 20,
                "AdminForge:MaxRedirects must be between 0 and 20.")
            .ValidateOnStart();

        services.AddSingleton<IOutboundTargetValidator, OutboundTargetValidator>();
        services.AddSingleton<SafeHttpFetcher>();
        services.AddSafeHttpClient();

        RegisterTools(services, toolAssemblies);

        return services;
    }

    private static void RegisterTools(IServiceCollection services, Assembly[] toolAssemblies)
    {
        IReadOnlyList<Type> toolTypes = ToolDiscovery.FindToolTypes(toolAssemblies);

        foreach (Type toolType in toolTypes)
        {
            // Scoped so a tool may depend on anything a request can, and so state held
            // in a field cannot leak between two users' runs.
            services.AddScoped(toolType);
        }

        services.AddSingleton<IToolRegistry>(sp =>
            new ToolRegistry(ToolDiscovery.BuildDescriptors(sp, toolTypes)));
    }

    /// <summary>
    /// Registers the <see cref="HttpClient"/> that every outbound tool request uses.
    /// <para>
    /// Redirects are handled by <see cref="SafeHttpFetcher"/> rather than the handler,
    /// so each hop can be re-validated, and the connect callback refuses private
    /// addresses at the socket — closing the window between a DNS check and the
    /// connection that a rebinding attack aims for.
    /// </para>
    /// </summary>
    private static IServiceCollection AddSafeHttpClient(this IServiceCollection services)
    {
        services
            .AddHttpClient(SafeHttpFetcher.ClientName, (sp, client) =>
            {
                AdminForgeOptions options = sp.GetRequiredService<IOptionsMonitor<AdminForgeOptions>>().CurrentValue;

                client.Timeout = TimeSpan.FromSeconds(options.ToolTimeoutSeconds);
                client.MaxResponseContentBufferSize = options.MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "AdminForge/1.0 (+https://github.com/juandresrodca/AdminForge)");
                client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                IOptionsMonitor<AdminForgeOptions> options =
                    sp.GetRequiredService<IOptionsMonitor<AdminForgeOptions>>();

                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    UseCookies = false,
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    ConnectCallback = (context, cancellationToken) =>
                        ConnectGuardedAsync(context, options.CurrentValue, cancellationToken),
                };
            });

        return services;
    }

    /// <summary>
    /// Resolves the target immediately before connecting and refuses any address the
    /// rules disallow, then connects only to addresses that passed.
    /// </summary>
    private static async ValueTask<Stream> ConnectGuardedAsync(
        SocketsHttpConnectionContext context,
        AdminForgeOptions options,
        CancellationToken cancellationToken)
    {
        DnsEndPoint endpoint = context.DnsEndPoint;

        IPAddress[] addresses = IPAddress.TryParse(endpoint.Host, out IPAddress? literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);

        IPAddress[] permitted = options.AllowPrivateTargets
            ? addresses
            : addresses.Where(a => !IpAddressRules.IsPrivateOrReserved(a)).ToArray();

        if (permitted.Length == 0)
        {
            throw new HttpRequestException(
                $"{endpoint.Host} resolves only to private or reserved addresses, which this instance will not contact.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(permitted, endpoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
