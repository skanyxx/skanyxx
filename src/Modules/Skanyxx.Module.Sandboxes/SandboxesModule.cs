using Microsoft.Extensions.Configuration;
using AxClient = Ax.V1Alpha1.AX.AXClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes;

/// <summary>
/// Experimental: run container tasks in Google AX (v0.3.1) sandboxes. AX is the only source of truth for task state —
/// there is no local run table; the creator is recorded on the AX task itself (see <see cref="TaskEnv"/>).
/// </summary>
public sealed class SandboxesModule : IModule
{
    public string ModuleId => "sandboxes";
    public string DisplayName => "Sandboxes (Experimental)";
    public string Version => "0.1.0";
    public IReadOnlyList<string> Dependencies => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Endpoint discovery cannot be skipped from here (the Host scans every loaded module), so SandboxesGroup
        // answers 404 on the same setting.
        if (!SandboxesOptions.IsEnabled(configuration))
            return;

        services.AddOptions<SandboxesOptions>()
            .Bind(configuration.GetSection(SandboxesOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => SandboxesOptions.IsHttpUrl(o.Address), "Sandboxes:Address must be an absolute http(s) URL.")
            .Validate(o => o.MemoryMcpUrl.Length == 0,
                "Sandboxes:MemoryMcpUrl must be empty: a sandbox would choose its own X-Agent-Id/X-User-Id on /mcp/memory. "
                + "It can be set once the sandbox listener with signed tokens exists (D076/D077, docs/design/ax-integration.md).")
            .Validate(o => o.AllowedImages.Length == 0 || o.NetworkIsolationConfirmed,
                "Sandboxes:AllowedImages needs Sandboxes:NetworkIsolationConfirmed=true: apply deploy/sandboxes/ (or equivalent) "
                + "first, so sandbox code cannot reach Skanyxx, ax-server or the cluster.")
            .Validate(o => o.AllowedImages.All(p => p.Length > 0), "Sandboxes:AllowedImages entries must not be empty.")
            .Validate(o => o.Supervisors.All(Identifier.IsValid), "Sandboxes:Supervisors entries must be valid user ids (lowercase, e.g. 'ana').")
            .Validate(o => o.HasValidResources(),
                "Sandboxes resource settings must be Kubernetes quantities with default request <= default limit <= max.")
            .ValidateOnStart();

        // An http:// address makes Grpc.Net.Client speak HTTP/2 with prior knowledge (h2c), which is what ax-server serves.
        // Not ThrowOperationCanceledOnCancellation: it also turns DeadlineExceeded into a cancellation (see AxErrors).
        services.AddGrpcClient<AxClient>((sp, o) =>
            o.Address = new Uri(sp.GetRequiredService<IOptions<SandboxesOptions>>().Value.Address));
        services.AddTransient<AxGateway>();
        services.AddSingleton<KeyedLock>();
        services.AddSingleton<WatchLimiter>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
