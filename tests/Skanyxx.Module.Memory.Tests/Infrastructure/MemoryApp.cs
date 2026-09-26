using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>
/// The memory module on real Kestrel (random loopback port), wired exactly like the Host
/// (platform extensions + module hooks). Kestrel rather than TestServer: it is what ships.
/// </summary>
public sealed class MemoryApp : IAsyncDisposable
{
    public const string Supervisor = "boss";
    public const string CorsControlPath = "/cors-control";
    public const string ApiFailurePath = "/api/test-failure";
    private const string AllowAll = "AllowAll";

    private readonly WebApplication _app;

    private MemoryApp(WebApplication app) => _app = app;

    public IServiceProvider Services => _app.Services;

    /// <summary>Every Error-level log line: an expected conflict must not be reported as a failure.</summary>
    public IReadOnlyList<string> Errors => _app.Services.GetRequiredService<ErrorLog>().Entries;

    // After start, Urls holds the port Kestrel actually bound.
    private Uri BaseAddress => new(_app.Urls.First());

    public static WebApplication Build(IDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        var errors = new ErrorLog();
        builder.Logging.AddProvider(errors);
        builder.Services.AddSingleton(errors);
        // Only these settings: the Host's appsettings.json is copied into this bin by the project reference.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var module = new MemoryModule();
        module.RegisterServices(builder.Services, builder.Configuration);
        builder.Services.AddSkanyxxPlatform([typeof(MemoryModule).Assembly]);
        // Same global policy as the Host, so the tests can prove memory endpoints opt out of it.
        builder.Services.AddCors(o => o.AddPolicy(AllowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseSkanyxxErrorHandling("/Error");
        app.UseRouting();
        app.UseCors(AllowAll);
        app.MapGet(CorsControlPath, () => "ok");
        app.MapGet(ApiFailurePath, IResult () => throw new InvalidOperationException("boom: internal detail"));
        // Stands in for the Host's Razor error page, so a mis-ordered handler would visibly serve HTML to the API.
        app.Map("/Error", () => Results.Content("<html>error page</html>", "text/html"));
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
            ["Memory:UpsertsPerMinuteTotal"] = upsertsPerMinuteTotal.ToString(),
            ["Memory:Supervisors:0"] = Supervisor
        });
        await app.StartAsync();
        return new MemoryApp(app);
    }

    public HttpClient Client(string? userId = null)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        if (userId is not null)
            client.DefaultRequestHeaders.Add("X-User-Id", userId);
        return client;
    }

    public async Task<McpClient> McpAsync(string? agentId, string? userId = null)
    {
        var headers = new Dictionary<string, string>();
        if (agentId is not null) headers["X-Agent-Id"] = agentId;
        if (userId is not null) headers["X-User-Id"] = userId;

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(BaseAddress, MemoryModule.McpPath),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = headers
        }, LoggerFactory.Create(_ => { }));
        return await McpClient.CreateAsync(transport);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
