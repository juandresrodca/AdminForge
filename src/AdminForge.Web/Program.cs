using System.Globalization;
using System.Threading.RateLimiting;
using AdminForge.Core;
using AdminForge.Core.Configuration;
using AdminForge.Tools;
using AdminForge.Web.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

IMvcBuilder mvc = builder.Services.AddControllersWithViews();

if (builder.Environment.IsDevelopment())
{
    // Editing a .cshtml and refreshing beats restarting the app — worth the extra
    // dependency for the people contributing tools.
    mvc.AddRazorRuntimeCompilation();
}

builder.Services.AddResponseCompression(o => o.EnableForHttps = false);

// One call registers the options, the hardened HTTP stack and every discovered tool.
// Adding a tool never changes this line.
builder.Services.AddAdminForge(builder.Configuration, typeof(ToolsAssembly).Assembly);
builder.Services.AddScoped<ToolInputBinder>();

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.AddPolicy(RateLimitPolicies.Tools, context =>
    {
        RateLimitOptions settings = context.RequestServices
            .GetRequiredService<IOptionsMonitor<AdminForgeOptions>>().CurrentValue.RateLimit;

        if (!settings.Enabled)
        {
            return RateLimitPartition.GetNoLimiter<string>("disabled");
        }

        // Partition by client address so one noisy user cannot starve the instance.
        string key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.PermitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = settings.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    });
});

// Behind a reverse proxy — the normal self-hosted setup — the client address arrives in a
// forwarded header. Without this the rate limiter would see one partition for everyone.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

WebApplication app = builder.Build();

// Results are formatted with the invariant culture so a tool gives the same answer
// regardless of the host's regional settings.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

// Tools and errors use attribute routes; the gallery uses the conventional one.
app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

// Liveness probe for container orchestrators and uptime monitors.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed so the integration tests can drive the real application.</summary>
public partial class Program;
