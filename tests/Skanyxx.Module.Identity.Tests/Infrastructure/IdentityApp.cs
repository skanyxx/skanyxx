using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>
/// The identity module on real Kestrel, wired like the Host: platform, API guards, cookie + bearer authentication and
/// the fallback policy. <see cref="ProbePath"/> is an ordinary protected API route that echoes the caller.
/// </summary>
public sealed class IdentityApp : IAsyncDisposable
{
    public const string ProbePath = "/api/probe";
    public const string OwnerEmail = "owner@skanyxx.example";
    public const string OwnerPassword = "correct horse battery";

    private readonly WebApplication _app;

    private IdentityApp(WebApplication app) => _app = app;

    public IServiceProvider Services => _app.Services;

    private Uri BaseAddress => new(_app.Urls.First());

    public const string BootstrapToken = "a-bootstrap-token-of-at-least-32-characters";

    public static WebApplication Build(
        IDictionary<string, string?> settings, string environment = "Development", Action<IServiceCollection>? services = null)
    {
        // Development also turns on DI scope validation, so a captive scoped dependency fails the build here.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var module = new IdentityModule();
        module.RegisterServices(builder.Services, builder.Configuration);
        builder.Services.AddSkanyxxPlatform([typeof(IdentityModule).Assembly]);
        builder.Services.AddSkanyxxApiGuards(builder.Configuration);
        builder.Services.AddSkanyxxAuthentication(builder.Environment);
        builder.Services.AddCors();
        // Last, so a test's replacement wins over the module's own registration.
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseSkanyxxApiGuards();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet(ProbePath, (HttpContext context) => new
        {
            userId = Caller.UserId(context.User),
            supervisor = Caller.IsSupervisor(context.User)
        });
        app.UseSkanyxxPlatform([module]);
        return app;
    }

    public static async Task<IdentityApp> StartAsync(
        string connectionString, Action<Dictionary<string, string?>>? configure = null, string environment = "Development",
        Action<IServiceCollection>? services = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = connectionString,
            ["Skanyxx:SignInRateLimit:PermitLimit"] = "10000"
        };
        configure?.Invoke(settings);
        var app = Build(settings, environment, services);
        await app.StartAsync();
        return new IdentityApp(app);
    }

    /// <summary>No cookie jar and no redirects: tests read and send cookies and see 302s themselves.</summary>
    public HttpClient Client(string? bearer = null, string? cookie = null)
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = BaseAddress };
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (cookie is not null)
            client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    public Task<HttpResponseMessage> BootstrapAsync(string email = OwnerEmail, string password = OwnerPassword, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/bootstrap")
        {
            Content = JsonContent.Create(new { email, password, displayName = "The Owner" })
        };
        if (token is not null)
            request.Headers.Add("X-Bootstrap-Token", token);
        return Client().SendAsync(request);
    }

    public Task<HttpResponseMessage> UnlockAsync(string email = OwnerEmail, string? token = BootstrapToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/unlock") { Content = JsonContent.Create(new { email }) };
        if (token is not null)
            request.Headers.Add("X-Bootstrap-Token", token);
        return Client().SendAsync(request);
    }

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        Client().PostAsJsonAsync("/api/identity/refresh", new { refreshToken });

    public Task<HttpResponseMessage> SignInAsync(
        string email = OwnerEmail, string password = OwnerPassword, bool useCookie = false, string? bootstrapToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/sign-in")
        {
            Content = JsonContent.Create(new { email, password, useCookie })
        };
        if (bootstrapToken is not null)
            request.Headers.Add("X-Bootstrap-Token", bootstrapToken);
        return Client().SendAsync(request);
    }

    public async Task<Tokens> SignInBearerAsync(string email = OwnerEmail, string password = OwnerPassword)
    {
        var response = await SignInAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Tokens.From(body.RootElement.GetProperty("tokens"));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
