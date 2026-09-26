using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Endpoints;
using Skanyxx.Module.Memory.Mcp;

namespace Skanyxx.Module.Memory;

/// <summary>Company memory engine: cards in Postgres, REST for the library, MCP for kagent (D016, D053).</summary>
public sealed class MemoryModule : IModule, IEndpointModule
{
    public const string McpPath = "/mcp/memory";

    public string ModuleId => "memory";
    public string DisplayName => "Memory";
    public string Version => "2.0.0";
    public IReadOnlyList<string> Dependencies => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Fail closed: a missing connection string stops startup instead of running without the bank.
        services.AddOptions<MemoryOptions>()
            .Bind(configuration.GetSection(MemoryOptions.Section))
            .Configure(o => o.ConnectionString = configuration.GetConnectionString("Memory") ?? "")
            .ValidateDataAnnotations()
            .Validate(o => o.Supervisors.All(Identifier.IsValid), "Memory:Supervisors entries must be valid user ids (lowercase, e.g. 'ana').")
            .ValidateOnStart();

        services.AddDbContext<MemoryDbContext>((sp, o) => o
            .UseNpgsql(sp.GetRequiredService<IOptions<MemoryOptions>>().Value.ConnectionSettings().ConnectionString)
            // A duplicate key on a stale create is an expected 409, not an error; a real failure still surfaces as an exception.
            .ConfigureWarnings(w => w.Log(
                (CoreEventId.SaveChangesFailed, LogLevel.Information),
                (RelationalEventId.CommandError, LogLevel.Information))));
        services.AddHostedService<MemoryMigrator>();
        services.AddScoped<CardSearch>();
        services.AddScoped<AccessPolicy>();
        services.AddSingleton<UpsertRateLimiter>();

        services.AddHealthChecks().AddNpgSql(
            sp => sp.GetRequiredService<IOptions<MemoryOptions>>().Value.ConnectionSettings().ConnectionString,
            name: "memory-postgres",
            tags: ["memory"]);

        services.AddHttpContextAccessor();
        services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<MemoryTools>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;

    // Same rules as MemoryGroup: CORS off (header identity must not be reachable from any web page) and the
    // body size capped — MCP parses the JSON-RPC body before it can see that the agent header is missing.
    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapMcp(McpPath).WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MemoryGroup.MaxBodyBytes));
}
