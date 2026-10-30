using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Analytics.Data;
using Skanyxx.Module.Analytics.Features;
using Skanyxx.Module.Analytics.Health;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Usage;

namespace Skanyxx.Module.Analytics;

/// <summary>
/// The admin Dashboard and Analytics (requirements.md §3.1, §3.11): a daily usage rollup per person, agent and model,
/// written from every chat turn (Core's <c>ChatTurnUsage</c>), reports scoped by the org tree, and the health of every
/// dependency. Memory's database; the org tree from identity, the default model from runtime, new cards from memory.
/// </summary>
public sealed class AnalyticsModule : IModule
{
    public string ModuleId => "analytics";
    public string DisplayName => "Dashboard and analytics";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => ["identity", "memory", "runtime"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.Section))
            .Configure(o => o.ConnectionString = configuration.GetConnectionString("Memory") ?? "");
        services.AddTokenUsage(configuration);

        services.AddDbContext<AnalyticsDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<IOptions<AnalyticsOptions>>().Value.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable(AnalyticsDbContext.MigrationsTable)));
        services.AddHostedService<AnalyticsMigrator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<UsageStore>();
        services.AddScoped<OrgReader>();
        services.AddScoped<HealthProbe>();
        services.AddSingleton<UsageReportBuilder>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
