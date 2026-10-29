using Skanyxx.Host.Diagnostics;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D353: the Debug page is the owner's diagnostics and nothing else: build and uptime, connectivity, configuration
/// with every secret redacted, recent warnings and request timings. Nobody else may open it.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class DebugPageTests(PostgresFixture fixture)
{
    private const string SmtpPassword = "smtp-pass-0f2d9c";
    private const string GitToken = "gittoken0b7e44c19a";
    private const string DragonflyPassword = "dfly-pass-7a1c";
    private const string SandboxKey = "sandbox-key-material-9d0e1f2a3b4c5d6e7f8091a2b3c4d5e6";

    [Fact]
    public async Task Owner_SeesDiagnostics_WithEverySecretRedacted()
    {
        var database = await fixture.NewDatabaseAsync();
        await using var host = await HostApp.StartAsync(database, s =>
        {
            HostApp.WithoutSandboxes(s);
            s["Identity:Smtp:Host"] = "smtp.example.invalid";
            s["Identity:Smtp:From"] = "skanyxx@example.invalid";
            s["Identity:Smtp:UserName"] = "skanyxx";
            s["Identity:Smtp:Password"] = SmtpPassword;
            s["Studio:Git:Token"] = GitToken;
            s["Memory:Dragonfly:Configuration"] = $"127.0.0.1:1,password={DragonflyPassword},abortConnect=false";
            s["Memory:SandboxTokenKey"] = SandboxKey;
        });
        var owner = await TestPeople.OwnerBrowserAsync(host);
        // A refused sign-in is a logged warning; the first Debug load is a timed request for the second.
        await new Browser(host).SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = "wrong password here" });
        await owner.GetAsync("/Debug");
        await owner.GetAsync("/api/investigate/runs/6f1c2d3e-0000-4000-8000-00000000beef");

        var html = await owner.HtmlAsync("/Debug");

        foreach (var section in new[] { "Build", "Connectivity", "Health checks", "Modules", "Recent warnings and errors", "Request timing", "Configuration" })
            Assert.Contains($">{section}</h2>", html);
        Assert.Contains("data-uptime", html);
        Assert.Contains("<td>kagent</td>", html);
        Assert.Contains("<td>Postgres (identity)</td>", html);
        Assert.Contains("<td>Dragonfly (memory)</td>", html);
        Assert.Contains("<td>AX (sandboxes)</td>", html); // off here: "not configured"
        Assert.Contains("not configured", html);
        Assert.Contains("GET /Debug", html);
        Assert.DoesNotContain("00000000beef", html); // timed by its route template, never the raw path (D377)
        Assert.Contains("<code>Identity:Smtp:Password</code>", html);
        Assert.Contains("<code>Studio:Git:Token</code>", html);
        Assert.Contains("<code>ConnectionStrings:Identity</code>", html);
        Assert.Contains("[redacted]", html);
        Assert.Contains("CommandLineConfigurationProvider", html);
        Assert.Contains("InstallationSettingsProvider", html);
        Assert.DoesNotContain("id=\"setup-debug\"", html); // set up: the panel shows no guide
        // Nothing that is not debugging: no quick commands, runners or tools.
        Assert.DoesNotContain("Quick command", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kubectl exec", html);
        foreach (var secret in new[] { SmtpPassword, GitToken, DragonflyPassword, SandboxKey, HostApp.BootstrapToken, database })
            Assert.DoesNotContain(secret, html);
        var password = new Npgsql.NpgsqlConnectionStringBuilder(database).Password!;
        Assert.DoesNotContain($"Password={password}", html);
    }

    [Fact]
    public async Task Debug_IsTheOwnersOnly()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), HostApp.WithoutSandboxes);
        var owner = await host.OwnerAsync();
        foreach (var role in new[] { "employee", "builder", "supervisor" })
        {
            var email = $"{role}@skanyxx.example";
            await TestPeople.InviteAsync(host, owner, email, role);
            var browser = await TestPeople.SignInAsync(host, email);

            var page = await browser.GetAsync("/Debug");

            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.Equal("/AccessDenied", new Uri(host.BaseAddress, page.Headers.Location!).AbsolutePath);
            Assert.DoesNotContain("href=\"/Debug\"", await browser.HtmlAsync("/Library"));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await host.Client().GetAsync("/Debug", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("Identity:Smtp:Password", "hunter2", true)]
    [InlineData("Studio:Git:Token", "abc", true)]
    [InlineData("Memory:SandboxTokenKey", "abc", true)]
    [InlineData("ConnectionStrings:Memory", "Host=db;Database=m", true)]
    [InlineData("Memory:Dragonfly:Configuration", "dfly:6379,password=x", true)]
    [InlineData("Studio:Git:BaseUrl", "https://user:pass@git.example", true)]
    [InlineData("Studio:Git:Pat", "abc", true)]
    [InlineData("Plugins:Sources:0:Pat", "abc", true)]
    [InlineData("Studio:Ci:WorkflowPath", ".gitea/workflows/validate-agents.yaml", false)]
    [InlineData("KAgent:BaseUrl", "http://kagent:8083", false)]
    [InlineData("Sandboxes:Enabled", "true", false)]
    public void ConfigurationValues_AreRedactedByKeyOrValue(string key, string value, bool redacted)
    {
        var shown = ConfigurationView.Display(key, value);

        if (redacted)
            Assert.DoesNotContain(value.Contains("pass") ? "pass@" : value, shown);
        else
            Assert.Equal(value, shown);
    }

    [Fact]
    public void RequestTimings_AreGroupedPerRouteTemplate_SlowestFirst()
    {
        var log = new DebugLog();
        foreach (var ms in new[] { 10.0, 20, 30, 40, 1000 })
            log.Emit(Request("GET", "/Library", 200, ms, "Library"));
        log.Emit(Request("POST", "/Org", 500, 5, "Org"));

        var timings = log.Timings(10);

        Assert.Equal(["/Library", "/Org"], timings.Select(t => t.Path));
        Assert.Equal(5, timings[0].Count);
        Assert.Equal(30, timings[0].MedianMs);
        Assert.Equal(1000, timings[0].P95Ms);
        Assert.Equal(1, timings[1].Errors);
    }

    [Fact]
    public void RequestTimings_NeverShowARawPath_ThatCouldCarryAKeyOrAnId()
    {
        var log = new DebugLog();
        log.Emit(Request("GET", "/api/memory/cards/sk-live-0123456789abcdef", 200, 3, "api/memory/cards/{key}"));
        log.Emit(Request("GET", "/api/memory/cards/someone@example.com", 200, 4, "api/memory/cards/{key}"));
        log.Emit(Request("GET", "/no/such/secret-path-4242", 404, 1, null));

        var timings = log.Timings(10);

        Assert.Equal([DebugLog.NoRoute, "/api/memory/cards/{key}"], timings.Select(t => t.Path).Order(StringComparer.Ordinal));
        Assert.Equal(2, timings.Single(t => t.Path == "/api/memory/cards/{key}").Count);
        Assert.DoesNotContain(timings, t => t.Path.Contains("sk-live") || t.Path.Contains('@') || t.Path.Contains("secret-path"));
    }

    private static Serilog.Events.LogEvent Request(string method, string path, int status, double ms, string? route) =>
        new(DateTimeOffset.UtcNow, Serilog.Events.LogEventLevel.Information, null, Serilog.Events.MessageTemplate.Empty,
        [
            new("RequestMethod", new Serilog.Events.ScalarValue(method)),
            new("RequestPath", new Serilog.Events.ScalarValue(path)),
            new("StatusCode", new Serilog.Events.ScalarValue(status)),
            new("Elapsed", new Serilog.Events.ScalarValue(ms)),
            .. route is null ? [] : new[] { new Serilog.Events.LogEventProperty(DebugLog.RouteProperty, new Serilog.Events.ScalarValue(route)) },
        ]);
}
