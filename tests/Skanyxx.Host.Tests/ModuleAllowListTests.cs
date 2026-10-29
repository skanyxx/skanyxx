using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D390: the loader takes only the modules this build ships. A stale <c>Skanyxx.Module.*.dll</c> from an older build
/// (CopyToModules never deletes) used to load and bring its removed routes back.
/// </summary>
public sealed class ModuleAllowListTests
{
    [Fact]
    public void TheKnownModules_AreExactlyTheModuleProjects()
    {
        var projects = Directory.GetDirectories(Repo.Path("src", "Modules"), "Skanyxx.Module.*")
            .Where(d => File.Exists(Path.Combine(d, Path.GetFileName(d) + ".csproj")))
            .Select(d => Path.GetFileName(d)["Skanyxx.Module.".Length..]);

        Assert.Equal(projects.Order(), ModuleLoader.KnownModules.Order());
    }

    [Fact]
    public void AStaleModuleDll_IsNotLoaded()
    {
        var dir = Directory.CreateTempSubdirectory("skanyxx-modules-");
        try
        {
            foreach (var file in Directory.GetFiles(Repo.Path("src", "Skanyxx.Host", "modules")))
                File.Copy(file, Path.Combine(dir.FullName, Path.GetFileName(file)));
            // A real, loadable module under a name this build no longer ships (the SRE console's Debug module).
            File.Copy(Path.Combine(dir.FullName, "Skanyxx.Module.Quotas.dll"), Path.Combine(dir.FullName, "Skanyxx.Module.Debug.dll"), overwrite: true);
            File.Copy(Path.Combine(dir.FullName, "Skanyxx.Module.Quotas.deps.json"), Path.Combine(dir.FullName, "Skanyxx.Module.Debug.deps.json"), overwrite: true);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:PluginDirectory"] = dir.FullName })
                .Build();
            var log = new ListLogger<ModuleLoader>();
            var loader = new ModuleLoader(config, log);
            loader.DiscoverModules(dir.FullName);

            Assert.Equal(ModuleLoader.KnownModules.Select(m => m.ToLowerInvariant()).Order(), loader.GetModules().Select(m => m.ModuleId).Order());
            // The module list is de-duplicated by id; the assemblies handed to MVC/FastEndpoints are not.
            Assert.DoesNotContain(loader.GetModuleAssemblies(), a => Path.GetFileName(a.Location) == "Skanyxx.Module.Debug.dll");
            Assert.Equal(ModuleLoader.KnownModules.Count, loader.GetModuleAssemblies().Count);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Not loading Skanyxx.Module.Debug.dll", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
