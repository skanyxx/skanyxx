using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

/// <summary>The sandboxes module on real Kestrel, wired like the Host (CORS allow-all globally), pointed at a fake AX.</summary>
public sealed class SandboxesApp : IAsyncDisposable
{
    public const string Supervisor = "boss";
    public const string User = "ana";
    public const string Other = "dan";
    public const string Atespace = "skanyxx";
    public const string AllowedImage = "ghcr.io/acme/";
    private const string AllowAll = "AllowAll";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebApplication _app;

    private SandboxesApp(WebApplication app, LogCapture logs) => (_app, Logs) = (app, logs);

    public LogCapture Logs { get; }

    private Uri BaseAddress => new(_app.Urls.First());

    public static async Task<SandboxesApp> StartAsync(string axAddress, Action<Dictionary<string, string?>>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        var logs = new LogCapture();
        builder.Logging.AddProvider(logs);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information); // "Request finished"
        var settings = new Dictionary<string, string?>
        {
            ["Sandboxes:Enabled"] = "true",
            ["Sandboxes:NetworkIsolationConfirmed"] = "true",
            ["Sandboxes:Address"] = axAddress,
            ["Sandboxes:Atespace"] = Atespace,
            ["Sandboxes:AllowedImages:0"] = AllowedImage,
            ["Sandboxes:MaxActiveTasksPerUser"] = "2",
            ["Sandboxes:Supervisors:0"] = Supervisor,
            ["Sandboxes:TimeoutSeconds"] = "5",
            // Generous: a watch window that closes early under a loaded full-solution run turns a frame test flaky.
            ["Sandboxes:WatchSeconds"] = "30"
        };
        configure?.Invoke(settings);
        foreach (var removed in settings.Where(s => s.Value is null).Select(s => s.Key).ToList())
            settings.Remove(removed);
        // Only these settings: the Host's appsettings.json is copied into this bin by the project reference.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var module = new SandboxesModule();
        module.RegisterServices(builder.Services, builder.Configuration);
        builder.Services.AddSkanyxxPlatform([typeof(SandboxesModule).Assembly]);
        builder.Services.AddCors(o => o.AddPolicy(AllowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseSkanyxxErrorHandling("/Error");
        app.UseRouting();
        app.UseCors(AllowAll);
        app.Map("/Error", () => Results.Content("<html>error page</html>", "text/html"));
        app.UseSkanyxxPlatform([module]);
        await app.StartAsync();
        return new SandboxesApp(app, logs);
    }

    public HttpClient Client(string? userId = User)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        if (userId is not null)
            client.DefaultRequestHeaders.Add("X-User-Id", userId);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
