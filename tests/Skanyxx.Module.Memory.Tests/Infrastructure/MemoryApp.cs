using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>
/// The memory module on real Kestrel (random loopback port), wired like the Host (platform extensions + module hooks
/// + authentication/authorization), with <see cref="TestAuthHandler"/> in place of the Host's cookie/bearer schemes.
/// Kestrel rather than TestServer: it is what ships.
/// </summary>
public sealed class MemoryApp : IAsyncDisposable
{
    public const string Supervisor = "boss";
    public const string Owner = "olivia";
    public const string CorsControlPath = "/cors-control";
    public const string ApiFailurePath = "/api/test-failure";
    private const string AllowAll = "AllowAll";

    private readonly WebApplication _app;
    private readonly Dictionary<string, (string Secret, bool ActsForUsers)> _secrets = [];

    private MemoryApp(WebApplication app) => _app = app;

    public IServiceProvider Services => _app.Services;

    /// <summary>Who is in which team (and so department); nobody is in anything until a test says so.</summary>
    public FakeOrgMembership Org => _app.Services.GetRequiredService<FakeOrgMembership>();

    /// <summary>Every Error-level log line: an expected conflict must not be reported as a failure.</summary>
    public IReadOnlyList<string> Errors => _app.Services.GetRequiredService<ErrorLog>().Entries;

    /// <summary>Every log entry at every level (the app logs at Trace).</summary>
    public string Logs => _app.Services.GetRequiredService<LogCapture>().All;

    // After start, Urls holds the port Kestrel actually bound.
    private Uri BaseAddress => new(_app.Urls.First());

    public static WebApplication Build(IDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        var errors = new ErrorLog();
        builder.Logging.AddProvider(errors);
        builder.Services.AddSingleton(errors);
        var logs = new LogCapture();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Services.AddSingleton(logs);
        // Only these settings: the Host's appsettings.json is copied into this bin by the project reference.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var module = new MemoryModule();
        module.RegisterServices(builder.Services, builder.Configuration);
        // The identity module implements IOrgMembership in the Host; memory is tested without it.
        builder.Services.AddSingleton<FakeOrgMembership>();
        builder.Services.AddSingleton<IOrgMembership>(sp => sp.GetRequiredService<FakeOrgMembership>());
        builder.Services.AddSkanyxxPlatform([typeof(MemoryModule).Assembly]);
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        // The Host's fallback: every endpoint without its own rule needs a signed-in user (e.g. /mcp/memory must opt out).
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        // Same global policy as the Host, so the tests can prove memory endpoints opt out of it.
        builder.Services.AddCors(o => o.AddPolicy(AllowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseSkanyxxErrorHandling("/Error");
        app.UseRouting();
        app.UseCors(AllowAll);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(CorsControlPath, () => "ok").AllowAnonymous();
        app.MapGet(ApiFailurePath, IResult () => throw new InvalidOperationException("boom: internal detail"));
        // Stands in for the Host's Razor error page, so a mis-ordered handler would visibly serve HTML to the API.
        app.Map("/Error", () => Results.Content("<html>error page</html>", "text/html")).AllowAnonymous();
        app.UseSkanyxxPlatform([module]);
        return app;
    }

    public static async Task<MemoryApp> StartAsync(string connectionString, int upsertsPerMinute = 1000, int upsertsPerMinuteTotal = 10_000)
    {
        var app = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = connectionString,
            ["Memory:SearchTopK"] = "5",
            ["Memory:UpsertsPerMinute"] = upsertsPerMinute.ToString(),
            ["Memory:UpsertsPerMinuteTotal"] = upsertsPerMinuteTotal.ToString()
        });
        await app.StartAsync();
        return new MemoryApp(app);
    }

    /// <summary>Signed in as <paramref name="userId"/> with <paramref name="roles"/>; no user means an anonymous client.</summary>
    public HttpClient Client(string? userId = null, params string[] roles)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        if (userId is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        return client;
    }

    public HttpClient SupervisorClient() => Client(Supervisor, SkanyxxRoles.Supervisor);

    public HttpClient OwnerClient() => Client(Owner, SkanyxxRoles.Owner);

    /// <summary>
    /// Issues (or rotates) the agent's secret and returns it: as the supervisor, or as the owner when the agent is to
    /// act for users (D084) or <paramref name="byOwner"/> asks for it (a team-granted agent's secret is the owner's, D092).
    /// </summary>
    public async Task<string> IssueSecretAsync(string agentId, bool actsForUsers = false, bool byOwner = false)
    {
        var client = actsForUsers || byOwner ? OwnerClient() : SupervisorClient();
        var response = await client.PostAsJsonAsync($"/api/memory/agents/{agentId}/secret", new { actsForUsers });
        response.EnsureSuccessStatusCode();
        var secret = (await response.JsonAsync()).GetProperty("secret").GetString()!;
        _secrets[agentId] = (secret, actsForUsers);
        return secret;
    }

    /// <summary>
    /// An MCP client for the agent, with its secret (issued on first use). With a user, the agent acts for users, so
    /// the user is honoured; without one it does not.
    /// </summary>
    public async Task<McpClient> McpAsync(string agentId, string? userId = null)
    {
        var actsForUsers = userId is not null;
        var secret = _secrets.TryGetValue(agentId, out var issued) && issued.ActsForUsers == actsForUsers
            ? issued.Secret
            : await IssueSecretAsync(agentId, actsForUsers);
        return await McpWithSecretAsync(secret, userId);
    }

    public async Task<McpClient> McpWithSecretAsync(string secret, string? userId = null, IDictionary<string, string>? headers = null)
    {
        var all = new Dictionary<string, string>(headers ?? new Dictionary<string, string>()) { ["Authorization"] = $"Bearer {secret}" };
        if (userId is not null) all["X-User-Id"] = userId;

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(BaseAddress, MemoryModule.McpPath),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = all
        }, LoggerFactory.Create(_ => { }));
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>One raw JSON-RPC POST to <c>/mcp/memory</c>, for asserting HTTP-level answers the MCP client hides.</summary>
    public Task<HttpResponseMessage> PostMcpAsync(string method, string? authorization, string? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MemoryModule.McpPath)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":""" + $"\"{method}\"" +
                ""","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
                System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (userId is not null)
            request.Headers.Add("X-User-Id", userId);
        return Client().SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
