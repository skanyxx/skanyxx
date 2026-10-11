using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Memory.Mcp;

/// <summary>
/// The other half of <see cref="McpPortMatcherPolicy"/>: a request that arrived on Memory:McpPort or on the sandbox
/// listener (Memory:SandboxMcpPort, D077) and is not for /mcp/memory gets 404 before anything else runs (static files,
/// pages, the sign-in fallback, /health), so those ports serve MCP and nothing else. Whoever is admitted to one — a
/// sandbox, above all — or publishes it by mistake, cannot reach the UI or the API there.
/// </summary>
internal sealed class McpPortOnlyFilter(IOptions<MemoryOptions> options) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        int[] ports = [.. new[] { options.Value.McpPort, options.Value.SandboxMcpPort }.OfType<int>()];
        if (ports.Length > 0)
        {
            app.Use((context, nextMiddleware) =>
            {
                if (!ports.Contains(context.Connection.LocalPort) || context.Request.Path.StartsWithSegments(MemoryModule.McpPath))
                    return nextMiddleware(context);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        }
        next(app);
    };
}
