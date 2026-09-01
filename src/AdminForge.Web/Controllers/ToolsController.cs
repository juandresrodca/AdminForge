using System.Diagnostics;
using AdminForge.Core.Configuration;
using AdminForge.Core.Registry;
using AdminForge.Core.Results;
using AdminForge.Core.Tools;
using AdminForge.Web.Infrastructure;
using AdminForge.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AdminForge.Web.Controllers;

/// <summary>
/// The single controller behind every tool. Tools are dispatched by id through the
/// registry, which is why adding one needs no new controller, no new route and no
/// change to this file.
/// </summary>
/// <param name="registry">The discovered tool catalogue.</param>
/// <param name="binder">Binds posted forms to tool input models.</param>
/// <param name="options">Deployment options, read for the per-run timeout.</param>
/// <param name="logger">Logger, used to record unexpected tool failures.</param>
[Route("tools")]
public sealed class ToolsController(
    IToolRegistry registry,
    ToolInputBinder binder,
    IOptionsMonitor<AdminForgeOptions> options,
    ILogger<ToolsController> logger) : Controller
{
    /// <summary>Header a fetch-based submission sends to ask for just the result fragment.</summary>
    private const string PartialHeader = "X-AdminForge-Partial";

    /// <summary>The tool page, with an empty form.</summary>
    /// <param name="id">The tool id from the URL.</param>
    [HttpGet("{id}")]
    public IActionResult Details(string id)
    {
        ToolDescriptor? tool = registry.Find(id);

        if (tool is null)
        {
            return RedirectToAction(nameof(ErrorController.Index), "Error", new { code = 404 });
        }

        // A canonical id keeps one URL per tool rather than one per casing.
        if (!string.Equals(tool.Id, id, StringComparison.Ordinal))
        {
            return RedirectPermanent(tool.Url);
        }

        return View(new ToolPageViewModel(tool));
    }

    /// <summary>Runs a server-side tool against the posted form.</summary>
    /// <param name="id">The tool id from the URL.</param>
    [HttpPost("{id}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Tools)]
    public async Task<IActionResult> Execute(string id)
    {
        ToolDescriptor? tool = registry.Find(id);

        if (tool is null)
        {
            return RedirectToAction(nameof(ErrorController.Index), "Error", new { code = 404 });
        }

        if (tool.Compute != ComputeMode.ServerSide)
        {
            // A client-side tool never posts. Reaching here means a crafted request.
            return BadRequest();
        }

        ToolInputBinding binding = binder.Bind(tool, Request.Form);

        if (!binding.IsValid)
        {
            return Render(new ToolPageViewModel(tool, null, binding.Errors, binding.Submitted));
        }

        ToolResult result = await RunAsync(tool, binding.Model!).ConfigureAwait(false);

        return Render(new ToolPageViewModel(tool, result, binding.Errors, binding.Submitted));
    }

    private async Task<ToolResult> RunAsync(ToolDescriptor tool, object input)
    {
        int timeoutSeconds = options.CurrentValue.ToolTimeoutSeconds;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        object instance = HttpContext.RequestServices.GetRequiredService(tool.ToolType);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            ToolResult result = await tool.ExecuteAsync(instance, input, timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            return result with { Elapsed = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            return ToolResult.Fail($"The tool did not finish within {timeoutSeconds} seconds and was stopped.");
        }
        catch (Exception ex)
        {
            // The tool id is logged so a failing contribution is trivial to attribute,
            // while the user sees nothing about the internals.
            logger.LogError(ex, "Tool {ToolId} threw an unhandled exception.", tool.Id);

            return ToolResult.Fail(
                "The tool failed unexpectedly. If this keeps happening, please open an issue with the input you used.");
        }
    }

    /// <summary>
    /// Returns just the result fragment for a fetch submission, or the whole page for
    /// a plain form post — so every tool works with JavaScript disabled.
    /// </summary>
    private IActionResult Render(ToolPageViewModel model) =>
        Request.Headers.ContainsKey(PartialHeader)
            ? PartialView("_ToolOutcome", model)
            : View(nameof(Details), model);
}
