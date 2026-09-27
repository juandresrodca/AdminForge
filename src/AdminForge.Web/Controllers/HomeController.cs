using AdminForge.Core.Registry;
using AdminForge.Core.Tools;
using AdminForge.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace AdminForge.Web.Controllers;

/// <summary>Serves the tool gallery.</summary>
/// <param name="registry">The discovered tool catalogue.</param>
public sealed class HomeController(IToolRegistry registry) : Controller
{
    /// <summary>The gallery, optionally filtered by search text and category.</summary>
    /// <param name="q">Free-text search.</param>
    /// <param name="category">Category slug, e.g. <c>network</c>.</param>
    [HttpGet]
    public IActionResult Index(string? q, string? category)
    {
        ToolCategory? selected = ToolCategories.FromSlug(category);

        IReadOnlyList<ToolDescriptor> tools = registry.Search(q);

        if (selected is not null)
        {
            tools = tools.Where(t => t.Category == selected).ToList();
        }

        return View(new GalleryViewModel(
            tools,
            registry.All.Count,
            q,
            selected,
            registry.Categories));
    }
}

/// <summary>Serves error pages for both handled statuses and unhandled exceptions.</summary>
[Route("error")]
public sealed class ErrorController : Controller
{
    /// <summary>Renders a friendly page for the given status code.</summary>
    /// <param name="code">The HTTP status code. Defaults to 500.</param>
    // Not limited to GET: the status-code pages middleware re-executes with the original
    // method, so a throttled or failed POST lands here as a POST and would otherwise
    // turn its 429 or 500 into a 405.
    [Route("")]
    [Route("{code:int}")]
    public IActionResult Index(int code = 500)
    {
        // The code comes from the URL, and Kestrel throws on a status outside 100-999.
        // Anything that is not an error status is shown as a generic failure.
        if (code is < 400 or > 599)
        {
            code = 500;
        }

        (string title, string message) = code switch
        {
            404 => ("Tool not found", "That tool does not exist. It may have been renamed — check the gallery."),
            429 => ("Slow down", "You have run tools faster than this instance allows. Try again in a moment."),
            400 => ("Bad request", "The request could not be understood."),
            _ => ("Something broke", "The tool failed unexpectedly. If it keeps happening, please open an issue."),
        };

        Response.StatusCode = code;
        return View(new ErrorViewModel(code, title, message));
    }
}
