using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Plugins.Features;
using Skanyxx.Module.Agents.Plugins.Sources;
using Skanyxx.Module.Agents.Services;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Plugins;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents;

/// <summary>
/// kagent agents. The studio (todo.md slice 3, D028–D032, D045): builders propose agents as pull requests in the agent
/// repo, supervisors merge, and the reconciler applies main to kagent and memory. The agents API only reads.
/// </summary>
public class AgentsModule : IModule
{
    public string ModuleId => "agents";
    public string DisplayName => "Agents";
    public string Version => "2.0.0";

    /// <summary>The reconciler writes grants and secrets through memory's studio contracts; the form lists identity's teams.</summary>
    public IReadOnlyList<string> Dependencies => ["memory", "identity"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAgentService, KAgentAgentService>();

        services.AddOptions<StudioOptions>().Bind(configuration.GetSection(StudioOptions.Section));
        services.AddHttpClient(GiteaAgentRepo.HttpClientName, (sp, client) =>
        {
            var git = sp.GetRequiredService<IOptions<StudioOptions>>().Value.Git;
            if (!string.IsNullOrWhiteSpace(git.BaseUrl))
                client.BaseAddress = new Uri(git.BaseUrl.TrimEnd('/') + "/api/v1/");
            // Per call deadlines (GitOptions.TimeoutSeconds); no retries: a proposal or a merge must not run twice.
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            if (!string.IsNullOrWhiteSpace(git.Token))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"token {git.Token}");
        });
        services.AddSingleton<IAgentRepo, GiteaAgentRepo>();
        services.AddSingleton<StudioRepoGuard>();
        services.AddSingleton<StudioSignal>();
        services.AddSingleton<PluginRepoReader>();
        services.AddSingleton<StudioReconciler>();
        services.AddHostedService<StudioReconcileLoop>();
        services.AddSingleton<ProposalInspector>();
        services.AddSingleton<StudioChat>();

        services.AddOptions<PluginsOptions>().Bind(configuration.GetSection(PluginsOptions.Section));
        services.AddHttpClient(GitArchiveSource.HttpClientName, client => client.Timeout = GitArchiveSource.Timeout)
            .ConfigurePrimaryHttpMessageHandler(sp => GitArchiveSource.Handler(sp.GetRequiredService<IOptions<PluginsOptions>>().Value));
        services.AddSingleton<GitArchiveSource>();
        services.AddSingleton<CatalogPluginSource>();
        services.AddSingleton<PluginLoader>();
    }

    /// <summary>
    /// The reconciler's cross-replica lock (D120) is memory's: fail startup, not the first pass, without it. A key ring in
    /// clear outside Development is said once at startup (D220): stored plugin header values depend on it. So is a plugin
    /// switch that widens where kagent's controller sends them (D229).
    /// </summary>
    public Task InitializeAsync(IServiceProvider serviceProvider)
    {
        if (!serviceProvider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IStudioReconcileLock)))
            throw new InvalidOperationException("The agents module needs IStudioReconcileLock (the memory module) to run the studio reconciler; enable the memory module.");
        var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<AgentsModule>();
        if (PluginValueKeyRing.InClear(environment, serviceProvider.GetRequiredService<IConfiguration>()))
            logger.LogWarning("{Warning}", PluginValueKeyRing.Warning);
        foreach (var warning in PluginEgressWarnings.Of(environment, serviceProvider.GetRequiredService<IOptions<PluginsOptions>>().Value))
            logger.LogWarning("{Warning}", warning);
        return Task.CompletedTask;
    }
}
