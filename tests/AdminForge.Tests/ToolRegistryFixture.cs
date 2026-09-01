using System.Reflection;
using AdminForge.Core;
using AdminForge.Core.Registry;
using AdminForge.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.Tests;

/// <summary>
/// Builds the real container and the real registry once for the whole test run, so
/// the contract tests exercise exactly what the application does at startup.
/// </summary>
public sealed class ToolRegistryFixture : IDisposable
{
    /// <summary>Creates the container and discovers every tool.</summary>
    public ToolRegistryFixture()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminForge:ToolTimeoutSeconds"] = "15",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAdminForge(configuration, typeof(ToolsAssembly).Assembly);

        Provider = services.BuildServiceProvider();
        Registry = Provider.GetRequiredService<IToolRegistry>();
    }

    /// <summary>The built container.</summary>
    public ServiceProvider Provider { get; }

    /// <summary>The discovered tool catalogue.</summary>
    public IToolRegistry Registry { get; }

    /// <summary>The repository root, found by walking up from the test assembly.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <inheritdoc />
    public void Dispose() => Provider.Dispose();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AdminForge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root from the test assembly.");
    }
}

/// <summary>Marks the tests that share <see cref="ToolRegistryFixture"/>.</summary>
[CollectionDefinition(Name)]
public sealed class ToolRegistryCollection : ICollectionFixture<ToolRegistryFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "tool-registry";
}
