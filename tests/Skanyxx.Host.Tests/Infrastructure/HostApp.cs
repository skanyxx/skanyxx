using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// The real Program.cs on real Kestrel (random loopback port), with every module from the Host's built
/// <c>modules/</c> folder. Settings go in as command-line arguments because Program reads configuration before
/// <c>Build()</c>. The content root is an empty temp directory, so the developer's local appsettings.json and
/// skanyxx.db are neither read nor written.
/// </summary>
public sealed class HostApp : IAsyncDisposable
{
    public const string AllowedOrigin = "https://skanyxx.example";
    public const string OwnerEmail = "owner@skanyxx.example";
    public const string OwnerPassword = "correct horse battery";

    /// <summary>The host runs as Production, where owner setup needs the bootstrap token.</summary>
    public const string BootstrapToken = "a-bootstrap-token-of-at-least-32-characters";

    private readonly IHost _host;
    private readonly Task _run;
    private readonly string _contentRoot;
    private string? _ownerToken;

    private HostApp(IHost host, Task run, string contentRoot) => (_host, _run, _contentRoot) = (host, run, contentRoot);

    public Uri BaseAddress => new(_host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

    public static async Task<HostApp> StartAsync(string connectionString, Action<Dictionary<string, string>>? configure = null)
    {
        var contentRoot = Directory.CreateTempSubdirectory("skanyxx-host-").FullName;
        var settings = new Dictionary<string, string>
        {
            ["environment"] = "Production",
            // The entry assembly is the test runner; MVC finds the Host's pages and controllers through this name.
            ["applicationName"] = typeof(Program).Assembly.GetName().Name!,
            ["contentRoot"] = contentRoot,
            // The real static files, so the tests see what is served without signing in.
            ["webroot"] = Repo.Path("src", "Skanyxx.Host", "wwwroot"),
            ["urls"] = "http://127.0.0.1:0",
            ["AllowedHosts"] = "localhost;127.0.0.1;[::1]",
            ["Skanyxx:AllowedOrigins:0"] = AllowedOrigin,
            ["Modules:PluginDirectory"] = Repo.Path("src", "Skanyxx.Host", "modules"),
            ["ConnectionStrings:Memory"] = connectionString,
            ["ConnectionStrings:Tickets"] = connectionString,
            ["ConnectionStrings:Identity"] = connectionString,
            ["Identity:BootstrapToken"] = BootstrapToken,
            // Tests sign in far more often than a person; SignInRateLimitTests sets its own window.
            ["Skanyxx:SignInRateLimit:PermitLimit"] = "10000",
            ["Tickets:Source"] = "local",
            ["Tickets:LocalPath"] = Repo.Path("deploy", "tickets", "sample-tickets.json"),
            // Nothing listens on port 1: kagent and AX calls fail fast.
            ["KAgent:BaseUrl"] = "127.0.0.1",
            ["KAgent:Port"] = "1",
            ["Sandboxes:Enabled"] = "true",
            ["Sandboxes:Address"] = "http://127.0.0.1:1",
            ["Sandboxes:TimeoutSeconds"] = "2"
        };
        configure?.Invoke(settings);
        string[] args = [.. settings.Select(s => $"--{s.Key}={s.Value}")];

        using var observer = new HostBuiltObserver();
        var run = Task.Run(() => typeof(Program).Assembly.EntryPoint!.Invoke(null, [args]));
        try
        {
            var host = await Ready(observer.Built, run);
            return new HostApp(host, run, contentRoot);
        }
        catch
        {
            Directory.Delete(contentRoot, recursive: true);
            throw;
        }
    }

    // Program.cs either reaches ApplicationStarted or its entry point throws (before or after Build()).
    private static async Task<IHost> Ready(Task<IHost> built, Task run)
    {
        if (await Task.WhenAny(built, run) == run)
            await RethrowAsync(run);
        var host = await built;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => started.SetResult());
        if (await Task.WhenAny(started.Task, run) == run)
            await RethrowAsync(run);
        return host;
    }

    private static async Task RethrowAsync(Task run)
    {
        try
        {
            await run;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
        }
        throw new InvalidOperationException("Program.cs exited before the host started.");
    }

    /// <summary>Anonymous. No cookie jar and no redirects, so tests see 302s and Set-Cookie themselves.</summary>
    public HttpClient Client(string? bearer = null)
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = BaseAddress };
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    public Task<HttpResponseMessage> BootstrapAsync(string? token = BootstrapToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/bootstrap")
        {
            Content = JsonContent.Create(new { email = OwnerEmail, password = OwnerPassword })
        };
        if (token is not null)
            request.Headers.Add("X-Bootstrap-Token", token);
        return Client().SendAsync(request);
    }

    /// <summary>Signed in as the owner with a bearer token; bootstraps the owner on first use.</summary>
    public async Task<HttpClient> OwnerAsync()
    {
        if (_ownerToken is null)
        {
            var status = await Client().GetFromJsonAsync<JsonElement>("/api/identity/status");
            if (!status.GetProperty("bootstrapped").GetBoolean())
            {
                var bootstrap = await BootstrapAsync();
                bootstrap.EnsureSuccessStatusCode();
            }

            var signIn = await Client().PostAsJsonAsync("/api/identity/sign-in", new { email = OwnerEmail, password = OwnerPassword });
            signIn.EnsureSuccessStatusCode();
            _ownerToken = (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString();
        }
        return Client(_ownerToken);
    }

    public async ValueTask DisposeAsync()
    {
        // app.Run() returns once the host stops, and disposes it.
        _host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await _run;
        Directory.Delete(_contentRoot, recursive: true);
    }
}
