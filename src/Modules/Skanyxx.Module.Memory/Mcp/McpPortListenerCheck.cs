using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Memory.Mcp;

/// <summary>
/// Warns once the server is listening when Memory:McpPort is none of its ports: /mcp/memory is then unreachable
/// everywhere (fail-closed), which otherwise looks like kagent being unable to connect for no reason.
/// </summary>
internal sealed partial class McpPortListenerCheck(
    IOptions<MemoryOptions> options, IServer server, IHostApplicationLifetime lifetime, ILogger<McpPortListenerCheck> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.McpPort is int port)
            lifetime.ApplicationStarted.Register(() => Check(port));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Check(int port)
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        if (!addresses.Any(a => BindingAddress.Parse(a).Port == port))
            NotListening(port, string.Join(", ", addresses));
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Memory:McpPort is {McpPort} but the server listens on {Addresses} only: /mcp/memory is unreachable. Add the port to ASPNETCORE_HTTP_PORTS (or urls), or unset Memory:McpPort.")]
    private partial void NotListening(int mcpPort, string addresses);
}
