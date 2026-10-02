using System.Reflection;
using Skanyxx.Host;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// The module ships only its own dll; everything else must resolve from the Host. A new package in the
/// module that is not in the shared list would load fine here and fail only inside the real plugin host.
/// </summary>
public sealed class PluginLoadingTests
{
    [Fact]
    public void EveryNonFrameworkDependency_IsSharedWithTheHost()
    {
        var frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var aspNetDir = Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location)!;

        var unshared = typeof(MemoryModule).Assembly.GetReferencedAssemblies()
            .Where(name => !IsFramework(name, frameworkDir, aspNetDir) && !PluginLoadContext.IsShared(name.Name))
            .Select(name => name.Name)
            .ToList();

        Assert.Empty(unshared);
    }

    /// <summary>The Host must ship each dependency at a version at least as high as the module was compiled against.</summary>
    [Fact]
    public void EverySharedDependency_IsShippedByTheHost_AtACompatibleVersion()
    {
        var hostRuntime = HostRuntimeAssemblies();
        var frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var aspNetDir = Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location)!;

        var problems = typeof(MemoryModule).Assembly.GetReferencedAssemblies()
            .Where(name => !IsFramework(name, frameworkDir, aspNetDir) && name.Name != "Skanyxx.Core")
            .Select(name => hostRuntime.TryGetValue(name.Name!, out var shipped)
                ? shipped >= name.Version! ? null : $"{name.Name}: module needs {name.Version}, host ships {shipped}"
                : $"{name.Name}: not shipped by the host")
            .OfType<string>()
            .ToList();

        Assert.NotEmpty(hostRuntime);
        Assert.Empty(problems);
    }

    /// <summary>Assembly name → version the Host ships at runtime, from its deps.json (copied next to the tests).</summary>
    private static Dictionary<string, Version> HostRuntimeAssemblies()
    {
        using var deps = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Skanyxx.Host.deps.json")));
        return deps.RootElement.GetProperty("targets").EnumerateObject().First().Value.EnumerateObject()
            .Where(library => library.Value.TryGetProperty("runtime", out _))
            .SelectMany(library => library.Value.GetProperty("runtime").EnumerateObject())
            .Where(asset => asset.Value.TryGetProperty("assemblyVersion", out _))
            .GroupBy(asset => Path.GetFileNameWithoutExtension(asset.Name))
            .ToDictionary(g => g.Key, g => g.Max(asset => Version.Parse(asset.Value.GetProperty("assemblyVersion").GetString()!))!);
    }

    [Theory]
    [InlineData("Npgsql", true)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", true)]
    [InlineData("HealthChecks.NpgSql", true)]
    [InlineData("NpgsqlEvil", false)]
    [InlineData("DapperPlus", false)]
    public void SharedPrefix_MatchesWholeNameSegments(string name, bool shared) =>
        Assert.Equal(shared, PluginLoadContext.IsShared(name));

    private static bool IsFramework(AssemblyName name, params string[] frameworkDirs)
    {
        var location = Assembly.Load(name).Location;
        return frameworkDirs.Any(dir => Path.GetDirectoryName(location) == dir);
    }
}
