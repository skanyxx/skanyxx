using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

/// <summary>
/// The sandboxes module on real Kestrel, wired like the Host (CORS allow-all globally), pointed at a fake AX.
/// <see cref="TestAuthHandler"/> stands in for the Host's cookie/bearer schemes.
/// </summary>
public sealed class SandboxesApp : IAsyncDisposable
{
    /// <summary>A user who holds the supervisor role (the role, not the name, is what grants the rights).</summary>
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
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        // The Host's fallback: every endpoint without its own rule needs a signed-in user (e.g. /mcp/memory must opt out).
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddCors(o => o.AddPolicy(AllowAll, p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseSkanyxxErrorHandling("/Error");
        app.UseRouting();
        app.UseCors(AllowAll);
        app.UseAuthentication();
        app.UseAuthorization();
        app.Map("/Error", () => Results.Content("<html>error page</html>", "text/html")).AllowAnonymous();
        app.UseSkanyxxPlatform([module]);
        await app.StartAsync();
        return new SandboxesApp(app, logs);
    }

    /// <summary>Signed in as <paramref name="userId"/>; <see cref="Supervisor"/> holds the supervisor role. Null: anonymous.</summary>
    public HttpClient Client(string? userId = User) =>
        ClientAs(userId, userId == Supervisor ? [SkanyxxRoles.Supervisor] : []);

    public HttpClient ClientAs(string? userId, params string[] roles)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        if (userId is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
