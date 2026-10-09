using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Memory.Mcp;

/// <summary>
/// The other half of <see cref="McpPortMatcherPolicy"/>: a request that arrived on Memory:McpPort and is not for
/// /mcp/memory gets 404 before anything else runs (static files, pages, the sign-in fallback), so the port serves MCP
/// and nothing else. Whoever is admitted to it, or publishes it by mistake, cannot reach the UI or the API there.
/// </summary>
internal sealed class McpPortOnlyFilter(IOptions<MemoryOptions> options) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (options.Value.McpPort is int port)
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Connection.LocalPort != port || context.Request.Path.StartsWithSegments(MemoryModule.McpPath))
                    return nextMiddleware(context);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        }
        next(app);
    };
}
