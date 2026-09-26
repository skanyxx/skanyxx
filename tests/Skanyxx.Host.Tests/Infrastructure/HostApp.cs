using System.Reflection;
using System.Runtime.ExceptionServices;
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
    public const string User = "ana";
    public const string AllowedOrigin = "https://skanyxx.example";

    private readonly IHost _host;
    private readonly Task _run;
    private readonly string _contentRoot;

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
            ["urls"] = "http://127.0.0.1:0",
            ["AllowedHosts"] = "localhost;127.0.0.1;[::1]",
            ["Skanyxx:AllowedOrigins:0"] = AllowedOrigin,
            ["Modules:PluginDirectory"] = Repo.Path("src", "Skanyxx.Host", "modules"),
            ["ConnectionStrings:Memory"] = connectionString,
            ["ConnectionStrings:Tickets"] = connectionString,
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

    public HttpClient Client(string? userId = User)
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        if (userId is not null)
            client.DefaultRequestHeaders.Add("X-User-Id", userId);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        // app.Run() returns once the host stops, and disposes it.
        _host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await _run;
        Directory.Delete(_contentRoot, recursive: true);
    }
}
