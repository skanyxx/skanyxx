using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Audit;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Engine;
using Skanyxx.Module.Investigate.Features;

namespace Skanyxx.Module.Investigate;

/// <summary>
/// Investigate (D340): workflows (trees of kagent agent steps, run in sequence or in parallel), runs executed by a
/// background worker with live per-step progress, cancel and per-step timeouts, JSON and PDF reports, and a memory card
/// per finding. Needs identity (teams, the audit) and memory (the card, its database).
/// </summary>
public sealed class InvestigateModule : IModule
{
    public string ModuleId => "investigate";
    public string DisplayName => "Investigate";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => ["identity", "memory"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InvestigateOptions>()
            .Bind(configuration.GetSection(InvestigateOptions.Section))
            .Configure(o => o.ConnectionString = configuration.GetConnectionString("Memory") ?? "")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContextFactory<InvestigateDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<IOptions<InvestigateOptions>>().Value.ConnectionSettings().ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable(InvestigateDbContext.MigrationsTable)));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<InvestigateDbContext>>().CreateDbContext());
        services.TryAddSingleton(TimeProvider.System);

        // Order matters: the migrator finishes before the worker reads runs.
        services.AddHostedService<InvestigateMigrator>();
        services.AddHostedService<InvestigationWorker>();

        services.AddSingleton<RunSignal>();
        services.AddSingleton<StepWriter>();
        services.AddSingleton<IInvestigationAgents, OfferedAgents>();
        services.AddScoped<RunExecutor>();
        // Unlimited until the quotas module replaces it; registered here too, as Agents and Tickets do, so Investigate runs without Chat.
        services.TryAddScoped<IChatQuota, UnlimitedChatQuota>();
        services.AddTokenUsage(configuration);
        services.AddScoped<InvestigateAudit>();
        services.AddScoped<OrgTeams>();
        services.AddScoped<AgentCheck>();
        services.AddScoped<ChainedWrites>();

        var kagent = configuration.GetSection("KAgent").Get<KAgentConfig>() ?? new KAgentConfig();
        services.AddHttpClient<IInvestigationAgentClient, KAgentInvestigationClient>(http =>
        {
            http.BaseAddress = new Uri(kagent.GetFullUrl().TrimEnd('/') + "/");
            // Each step has its own deadline (Investigate:StepTimeoutSeconds), so HttpClient's must not cut it first.
            http.Timeout = Timeout.InfiniteTimeSpan;
            // Bounds what one reply can make us buffer; the answer cap applies after parsing.
            http.MaxResponseContentBufferSize = 8 * 1024 * 1024;
            if (!string.IsNullOrEmpty(kagent.Token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kagent.Token);
        });
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
