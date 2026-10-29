using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skanyxx.Core;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Quotas.Data;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas;

/// <summary>
/// Monthly token quotas per person, team and department (the tightest one binds), what each person used, requests for
/// more and the owner's per-model prices. Replaces the unlimited <see cref="IChatQuota"/> (Chat, Studio, Tickets). Memory's database; the org
/// tree from identity.
/// </summary>
public sealed class QuotasModule : IModule
{
    public string ModuleId => "quotas";
    public string DisplayName => "Token quotas";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Dependencies => ["identity", "memory"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Memory") ?? "";
        services.AddDbContext<QuotaDbContext>(o => o.UseNpgsql(connectionString,
            npgsql => npgsql.MigrationsHistoryTable(QuotaDbContext.MigrationsTable)));
        services.AddHostedService<QuotaMigrator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<QuotaStore>();
        services.AddScoped<QuotaLedger>();
        services.AddScoped<OrgReader>();
        services.AddSingleton(sp => new QuotaInFlight(sp.GetRequiredService<TimeProvider>(), QuotaInFlight.ExpiryFor(configuration)));
        // Not TryAdd: this replaces Chat's unlimited default whichever module registers first.
        services.AddScoped<IChatQuota, DbChatQuota>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
