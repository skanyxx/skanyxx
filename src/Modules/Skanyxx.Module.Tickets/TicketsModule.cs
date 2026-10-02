using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Engine;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets;

/// <summary>
/// Ticket pipelines (ported from Umbrella.Flow): Skanyxx orchestrates — stage order, verdicts, bounded loops,
/// human gates, run history — and each stage is one turn of a kagent Agent over A2A. Read-only toward Jira.
/// </summary>
public sealed class TicketsModule : IModule
{
    public string ModuleId => "tickets";
    public string DisplayName => "Tickets";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TicketsOptions>()
            .Bind(configuration.GetSection(TicketsOptions.Section))
            .Configure(o => o.ConnectionString = configuration.GetConnectionString("Tickets") ?? "")
            .PostConfigure(o => o.AllowedAgents = o.AllowedAgents.Length > 0 ? o.AllowedAgents : DefaultPipeline.Agents)
            .ValidateDataAnnotations()
            .Validate(o => o.Source != "local" || File.Exists(o.LocalPath), "Tickets:LocalPath must name an existing file when Source is 'local'.")
            .ValidateOnStart();

        services.AddDbContext<TicketsDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<IOptions<TicketsOptions>>().Value.ConnectionSettings().ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable(TicketsDbContext.MigrationsTable)));
        services.TryAddSingleton(TimeProvider.System);

        // Order matters: the migrator finishes before the worker starts reading runs.
        services.AddHostedService<TicketsMigrator>();
        services.AddHostedService<RunWorker>();

        services.AddSingleton<IStageSkill, TicketFactsSkill>();
        services.AddSingleton<IStageSkill, PriorStageDigestSkill>();
        services.AddSingleton<IStageSkill, RiskChecklistSkill>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<RunSignal>();
        services.AddScoped<StageRunner>();
        services.AddScoped<RunStepper>();

        var kagent = configuration.GetSection("KAgent").Get<KAgentConfig>() ?? new KAgentConfig();
        services.AddHttpClient<IStageAgentClient, KAgentStageClient>((sp, http) =>
        {
            http.BaseAddress = new Uri(kagent.GetFullUrl().TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<TicketsOptions>>().Value.StageTimeoutSeconds);
            // Bounds what one reply can make us buffer; the 100k answer cap is applied after parsing.
            http.MaxResponseContentBufferSize = 8 * 1024 * 1024;
            if (!string.IsNullOrEmpty(kagent.Token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kagent.Token);
        });

        if (configuration[$"{TicketsOptions.Section}:Source"] == "local")
            services.AddSingleton<ITicketSource, LocalTicketSource>();
        else
            services.AddHttpClient<ITicketSource, JiraTicketSource>((sp, http) =>
                http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<TicketsOptions>>().Value.Jira.TimeoutSeconds));

        services.AddHealthChecks().AddNpgSql(
            sp => sp.GetRequiredService<IOptions<TicketsOptions>>().Value.ConnectionSettings().ConnectionString,
            name: "tickets-postgres",
            tags: ["tickets"]);
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
