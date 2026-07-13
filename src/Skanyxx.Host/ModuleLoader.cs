using System.Reflection;
using System.Runtime.Loader;
using Skanyxx.Core;

namespace Skanyxx.Host;

public class ModuleLoader
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModuleLoader> _logger;
    private readonly List<IModule> _modules = new();
    private readonly List<Assembly> _moduleAssemblies = new();

    public ModuleLoader(IConfiguration configuration, ILogger<ModuleLoader> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void DiscoverModules(string contentRootPath)
    {
        var pluginDir = _configuration["Modules:PluginDirectory"] ?? "modules";
        var modulesPath = Path.IsPathRooted(pluginDir)
            ? pluginDir
            : Path.Combine(contentRootPath, pluginDir);

        if (!Directory.Exists(modulesPath))
        {
            _logger.LogWarning("Module directory not found: {Path}. Creating it.", modulesPath);
            Directory.CreateDirectory(modulesPath);
            return;
        }

        var dlls = Directory.GetFiles(modulesPath, "Skanyxx.Module.*.dll");
        _logger.LogInformation("Found {Count} module DLLs in {Path}", dlls.Length, modulesPath);

        foreach (var dll in dlls)
        {
            try
            {
                var loadContext = new PluginLoadContext(dll);
                var assembly = loadContext.LoadFromAssemblyPath(dll);

                var moduleType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                if (moduleType == null)
                {
                    _logger.LogWarning("No IModule implementation found in {Dll}", Path.GetFileName(dll));
                    continue;
                }

                var module = (IModule)Activator.CreateInstance(moduleType)!;

                // Check if module is enabled
                var enabledKey = $"Modules:Enabled:{module.ModuleId}";
                var enabled = _configuration.GetValue<bool?>(enabledKey) ?? true;

                if (!enabled)
                {
                    _logger.LogInformation("Module '{ModuleId}' is disabled, skipping", module.ModuleId);
                    continue;
                }

                _modules.Add(module);
                _moduleAssemblies.Add(assembly);
                _logger.LogInformation("Discovered module: {DisplayName} v{Version} ({ModuleId})",
                    module.DisplayName, module.Version, module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load module from {Dll}", Path.GetFileName(dll));
            }
        }

        // Topological sort by dependencies
        SortByDependencies();
    }

    private void SortByDependencies()
    {
        var sorted = new List<IModule>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(IModule module)
        {
            if (visited.Contains(module.ModuleId)) return;
            if (visiting.Contains(module.ModuleId))
            {
                _logger.LogWarning("Circular dependency detected for module {ModuleId}", module.ModuleId);
                return;
            }

            visiting.Add(module.ModuleId);

            foreach (var dep in module.Dependencies)
            {
                var depModule = _modules.FirstOrDefault(m => m.ModuleId == dep);
                if (depModule != null)
                    Visit(depModule);
            }

            visiting.Remove(module.ModuleId);
            visited.Add(module.ModuleId);
            sorted.Add(module);
        }

        foreach (var module in _modules)
            Visit(module);

        _modules.Clear();
        _modules.AddRange(sorted);
    }

    public IReadOnlyList<Assembly> GetModuleAssemblies() => _moduleAssemblies.AsReadOnly();
    public IReadOnlyList<IModule> GetModules() => _modules.AsReadOnly();

    public void RegisterAllModuleServices(IServiceCollection services, IConfiguration configuration)
    {
        foreach (var module in _modules)
        {
            try
            {
                module.RegisterServices(services, configuration);
                _logger.LogInformation("Registered services for module: {ModuleId}", module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register services for module: {ModuleId}", module.ModuleId);
            }
        }
    }

    public async Task InitializeAllModulesAsync(IServiceProvider serviceProvider)
    {
        foreach (var module in _modules)
        {
            try
            {
                await module.InitializeAsync(serviceProvider);
                _logger.LogInformation("Initialized module: {ModuleId}", module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize module: {ModuleId}", module.ModuleId);
            }
        }
    }
}
