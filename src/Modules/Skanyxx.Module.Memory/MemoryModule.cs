using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
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

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentSecretAuthentication>(AgentSecretAuthentication.SchemeName, null);
        services.AddHttpContextAccessor();
        services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<MemoryTools>();
    }

    public Task InitializeAsync(IServiceProvider serviceProvider) => Task.CompletedTask;

    // kagent has no signed-in user, so the MCP route does not use the Host's user schemes: it requires an agent secret
    // (D080) and nothing else, and the agent is whoever owns that secret. The 401 comes from authorization, before MCP
    // parses anything. CORS stays off (no web page should drive it) and the body is capped.
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var mcp = endpoints.MapGroup(McpPath)
            .RequireAuthorization(p => p.AddAuthenticationSchemes(AgentSecretAuthentication.SchemeName).RequireAuthenticatedUser())
            .WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MemoryGroup.MaxBodyBytes));
        mcp.MapMcp();
        // Stateless MCP maps POST only. Clients (kagent's go-sdk) still open the standalone SSE stream (GET) and end the
        // session (DELETE); unmapped, any other verb falls to the framework's 405 endpoint, which has no authorization
        // metadata, so the Host's user policy answered a valid secret with 401. MCP Streamable HTTP allows 405 here.
        // Only while Stateless: a stateful MapMcp maps GET/DELETE itself and these would become ambiguous.
        mcp.MapMethods("", [HttpMethods.Get, HttpMethods.Delete, HttpMethods.Head, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Options],
            (HttpContext context) =>
            {
                context.Response.Headers.Allow = HttpMethods.Post;
                return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
            });
    }
}
