using System.Reflection;
using System.Runtime.Loader;

namespace Skanyxx.Host;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Don't load shared assemblies - let them resolve from the default context
        // This ensures modules share the same Core types as the host
        var sharedAssemblies = new[] { "Skanyxx.Core", "MediatR", "Microsoft.Extensions" };
        if (sharedAssemblies.Any(sa => assemblyName.Name?.StartsWith(sa) == true))
            return null;

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
