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

    /// <summary>
    /// Types that cross the host/module boundary (DI, endpoints, validation, EF, MCP, gRPC) must come from
    /// the default context, or a module sees a second copy of the same type. A module only ships its
    /// own dll, so every dependency matched here must also be referenced by the Host.
    /// </summary>
    public static readonly string[] SharedAssemblies =
    [
        "Skanyxx.Core", "MediatR", "Microsoft.Extensions", "Microsoft.EntityFrameworkCore",
        "Npgsql", "Dapper", "FastEndpoints", "FluentValidation", "ModelContextProtocol", "HealthChecks",
        "Grpc", "Google.Protobuf"
    ];

    public static bool IsShared(string? name) =>
        name is not null && SharedAssemblies.Any(p =>
            name.Equals(p, StringComparison.OrdinalIgnoreCase) || name.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName.Name))
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
