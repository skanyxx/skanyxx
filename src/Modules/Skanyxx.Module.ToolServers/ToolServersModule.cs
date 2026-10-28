using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Skanyxx.Core;
using Skanyxx.Module.ToolServers.Data;
using Skanyxx.Module.ToolServers.Features;

namespace Skanyxx.Module.ToolServers;

/// <summary>
/// The Tool servers page's API (<c>api/toolservers</c>) over kagent's controller: list, add, edit, delete and refresh
/// RemoteMCPServers, with a fix for every part that is not working. What Skanyxx sent kagent (never a header value) is
/// kept in memory's database, because kagent 0.10.2 lists no server's URL. Needs memory (the studio's servers, its
/// database); memory itself needs identity, whose audit records every write.
/// </summary>
public sealed class ToolServersModule : IModule
{
    public string ModuleId => "toolservers";
    public string DisplayName => "Tool Servers";
    public string Version => "2.0.0";
    public IReadOnlyList<string> Dependencies => ["memory"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Memory"));
        connection.ApplicationName = $"{(string.IsNullOrEmpty(connection.ApplicationName) ? "skanyxx" : connection.ApplicationName)}-toolservers";
        services.AddDbContext<ToolServersDbContext>(o => o.UseNpgsql(connection.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable(ToolServersDbContext.MigrationsTable)));
        services.AddHostedService<ToolServersMigrator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ToolServerCatalog>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;
}
