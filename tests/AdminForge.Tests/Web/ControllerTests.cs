using AdminForge.Core.Configuration;
using AdminForge.Web.Controllers;
using AdminForge.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AdminForge.Tests.Web;

/// <summary>Status codes the controllers hand back for bad URLs.</summary>
[Collection(ToolRegistryCollection.Name)]
public sealed class ControllerTests(ToolRegistryFixture fixture)
{
    [Theory]
    [InlineData(99999, 500)]
    [InlineData(0, 500)]
    [InlineData(200, 500)]
    [InlineData(404, 404)]
    [InlineData(429, 429)]
    public void The_error_page_only_sets_error_statuses(int requested, int expected)
    {
        var controller = new ErrorController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        controller.Index(requested);

        Assert.Equal(expected, controller.Response.StatusCode);
    }

    [Fact]
    public void An_unknown_tool_is_a_404_not_a_redirect()
    {
        var controller = new ToolsController(
            fixture.Registry,
            new ToolInputBinder(),
            fixture.Provider.GetRequiredService<IOptionsMonitor<AdminForgeOptions>>(),
            NullLogger<ToolsController>.Instance);

        Assert.IsType<NotFoundResult>(controller.Details("no-such-tool"));
    }

    [Fact]
    public void The_error_page_answers_any_method_so_a_rejected_post_keeps_its_status()
    {
        System.Reflection.MethodInfo index = typeof(ErrorController).GetMethod(nameof(ErrorController.Index))!;

        Assert.Empty(index.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), inherit: true));
    }
}
