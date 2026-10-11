using Microsoft.Extensions.Configuration;
using AxClient = Ax.V1Alpha1.AX.AXClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Sandboxes.Features;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes;

/// <summary>
/// Experimental: run containers in Google AX (v0.3.1) sandboxes. AX is the only source of truth for sandbox state —
/// there is no local run table; the creator is recorded on the sandbox itself (see <see cref="SandboxEnv"/>).
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
        services.AddSingleton<KeyedLock>();
        services.AddSingleton<WatchLimiter>();
        services.AddTransient<MemoryAccess>();

        // MediatR registers the handlers either way, and Development's ValidateOnBuild resolves them.
        if (!SandboxesOptions.IsEnabled(configuration))
        {
            services.AddTransient<AxGateway>(_ => throw new InvalidOperationException("Sandboxes are disabled (Sandboxes:Enabled)."));
            return;
        }

        services.AddOptions<SandboxesOptions>()
            .Bind(configuration.GetSection(SandboxesOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => SandboxesOptions.IsHttpUrl(o.Address), "Sandboxes:Address must be an absolute http(s) URL.")
            .Validate(o => o.MemoryMcpUrl.Length == 0 || SandboxesOptions.IsSandboxListener(o.MemoryMcpUrl, configuration),
                "Sandboxes:MemoryMcpUrl must be empty or the sandbox listener's /mcp/memory: http(s)://<host>:<Memory:SandboxMcpPort>/mcp/memory "
                + $"(no credentials, query or fragment), with Memory:SandboxTokenKey set ({SandboxMemory.MinTokenKeyChars}+ characters). "
                + "Any other port serves agent secrets or the whole API, and a sandbox must reach neither (D076/D077, docs/design/ax-integration.md).")
            .Validate(o => o.AllowedImages.Length == 0 || o.NetworkIsolationConfirmed,
                "Sandboxes:AllowedImages needs Sandboxes:NetworkIsolationConfirmed=true: apply deploy/sandboxes/ (or equivalent) "
                + "first, so sandbox code cannot reach Skanyxx, ax-server or the cluster.")
            .Validate(o => o.AllowedImages.All(p => p.Length > 0), "Sandboxes:AllowedImages entries must not be empty.")
            .Validate(o => o.HasValidResources(),
                "Sandboxes resource settings must be Kubernetes quantities with default request <= default limit <= max.")
            .ValidateOnStart();

        // An http:// address makes Grpc.Net.Client speak HTTP/2 with prior knowledge (h2c), which is what ax-server serves.
        // Not ThrowOperationCanceledOnCancellation: it also turns DeadlineExceeded into a cancellation (see AxErrors).
        services.AddGrpcClient<AxClient>((sp, o) =>
            o.Address = new Uri(sp.GetRequiredService<IOptions<SandboxesOptions>>().Value.Address));
        services.AddTransient<AxGateway>();
    }

    /// <summary>Memory attach needs memory to sign the tokens: fail startup, not the first run, when it is not loaded.</summary>
    public Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var memoryOn = SandboxesOptions.IsEnabled(configuration)
            && !string.IsNullOrEmpty(configuration[$"{SandboxesOptions.Section}:{nameof(SandboxesOptions.MemoryMcpUrl)}"]);
        return memoryOn && !serviceProvider.GetRequiredService<IServiceProviderIsService>()
                .IsService(typeof(IRequestHandler<IssueSandboxMemoryTokenCommand, Outcome<SandboxMemoryToken>>))
            ? throw new InvalidOperationException("Sandboxes:MemoryMcpUrl is set but the memory module, which signs sandbox tokens, is not loaded.")
            : Task.CompletedTask;
    }
}
