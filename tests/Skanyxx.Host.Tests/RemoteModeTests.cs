using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Host.Data;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Remote mode (D201): the desktop launcher sends the browser to the company's Skanyxx and holds nothing itself — no
/// database, no modules, no sign-in. It redirects to the configured address only, never to a path taken from the request.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class RemoteModeTests(PostgresFixture fixture)
{
    private const string RemoteUrl = "https://skanyxx.example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Nothing listens on port 1: a remote host that touched a database could not start.
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=skanyxx;Username=skanyxx;Password=x;Timeout=3";

    private static Task<HostApp> StartRemoteAsync(string remoteUrl = RemoteUrl) => HostApp.StartAsync(Unreachable, s =>
    {
        s["Skanyxx:Mode"] = "Remote";
        s["Skanyxx:RemoteUrl"] = remoteUrl;
    });

    [Theory]
    [InlineData("/")]
    [InlineData("/Library")]
    [InlineData("/api/memory/cards?scope=company")]
    [InlineData("/mcp/memory")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task GetAndHead_RedirectToTheRemoteRoot_Only(string path)
    {
        await using var host = await StartRemoteAsync();
        var client = host.Client();

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            var response = await client.SendAsync(new HttpRequestMessage(method, path), Ct);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal(new Uri(RemoteUrl + "/"), response.Headers.Location);
        }
    }

    [Theory]
    [InlineData("POST", "/api/identity/sign-in")]
    [InlineData("POST", "/mcp/memory")]
    [InlineData("PUT", "/api/memory/cards")]
    [InlineData("DELETE", "/")]
    [InlineData("POST", "/health")]
    public async Task OtherMethods_Are404(string method, string path)
    {
        await using var host = await StartRemoteAsync();

        var response = await host.Client().SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { email = "a@b.c", password = "x" })
        }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Health_SaysRemote()
    {
        await using var host = await StartRemoteAsync();

        var response = await host.Client().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("remote", body.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task HoldsNoDatabase_NoModules_NoSignIn()
    {
        await using var host = await StartRemoteAsync();
        await host.Client().GetAsync("/", Ct);
        await host.Client().GetAsync("/health", Ct);

        Assert.False(File.Exists(Path.Combine(host.ContentRoot, "skanyxx.db")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(host.ContentRoot));
        Assert.Null(host.Services.GetService<AppDbContext>());
        Assert.Null(host.Services.GetService<IMediator>());
        Assert.Null(host.Services.GetService<IAuthenticationSchemeProvider>());
    }

    [Fact]
    public async Task WithoutRemoteUrl_RootExplainsWhereToSetIt_EverythingElseIs404()
    {
        await using var host = await StartRemoteAsync(remoteUrl: "");
        var client = host.Client();

        var root = await client.GetAsync("/", Ct);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        var page = await root.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"RemoteUrl\"", page);
        Assert.Contains(Path.Combine(host.ContentRoot, "appsettings.json"), page);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Library", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("skanyxx.example.com")]
    [InlineData("/relative")]
    [InlineData("ftp://skanyxx.example.com")]
    [InlineData("http://skanyxx.example.com")]
    [InlineData("http://localhost.example.com")]
    [InlineData("https://skanyxx.example.com/?next=/x")]
    [InlineData("https://skanyxx.example.com/#x")]
    [InlineData("https://user:pass@skanyxx.example.com")]
    [InlineData(" https://skanyxx.example.com")]
    [InlineData("https://skanyxx.bücher.example")] // a Unicode host would make every redirect a 500
    [InlineData("https://skanyxx.example.com/tëam")]
    public async Task AnInvalidRemoteUrl_ExitsOne(string remoteUrl)
    {
        var (exitCode, log, _) = await ExitCodeTests.RunEntryPointAsync(Unreachable, s =>
        {
            s["Skanyxx:Mode"] = "Remote";
            s["Skanyxx:RemoteUrl"] = remoteUrl;
        });

        Assert.Equal(1, exitCode);
        Assert.Contains(log.Split('\n'), line => line.Contains("\"@l\":\"Fatal\"") && line.Contains("Skanyxx:RemoteUrl"));
    }

    [Fact]
    public async Task AnUnknownMode_ExitsOne()
    {
        var (exitCode, log, _) = await ExitCodeTests.RunEntryPointAsync(Unreachable, s => s["Skanyxx:Mode"] = "Bogus");

        Assert.Equal(1, exitCode);
        Assert.Contains(log.Split('\n'), line => line.Contains("\"@l\":\"Fatal\"") && line.Contains("Skanyxx:Mode"));
    }

    [Fact]
    public async Task AnInternationalisedHost_InItsPunycodeForm_IsAccepted()
    {
        await using var host = await StartRemoteAsync("https://skanyxx.xn--bcher-kva.example");

        var response = await host.Client().GetAsync("/", Ct);

        Assert.Equal(new Uri("https://skanyxx.xn--bcher-kva.example/"), response.Headers.Location);
    }

    [Fact]
    public async Task HttpToLoopback_IsAccepted()
    {
        await using var host = await StartRemoteAsync("http://localhost:8080");

        var response = await host.Client().GetAsync("/Chat", Ct);

        Assert.Equal(new Uri("http://localhost:8080/"), response.Headers.Location);
    }

    [Fact]
    public async Task ServerMode_IsTheFullHost_AndWarnsAboutADatabaseOnThisMachine()
    {
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        HostApp host;
        try
        {
            host = await HostApp.StartAsync(fixture.ConnectionString, s => s["Skanyxx:Mode"] = "server");
        }
        finally
        {
            Console.SetOut(original);
        }

        await using (host)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.Client().GetAsync("/api/identity/status", Ct)).StatusCode);
            Assert.NotNull(host.Services.GetService<IMediator>());
        }
        Assert.Contains(console.ToString().Split('\n'),
            line => line.Contains("\"@l\":\"Warning\"") && line.Contains("dev console") && line.Contains("ConnectionStrings:Identity"));
    }

    /// <summary>The installers turn a fresh config into Remote mode by editing the template's text: keep the two in step.</summary>
    [Fact]
    public void TheInstallers_TurnTheTemplateIntoRemoteMode()
    {
        var template = File.ReadAllText(Repo.Path("appsettings.template.json"));
        Assert.Equal(1, Regex.Count(template, "\"Mode\": \"Server\""));
        Assert.Equal(1, Regex.Count(template, "\"RemoteUrl\": \"\""));

        foreach (var script in new[] { Repo.Path("installer", "mac", "scripts", "postinstall"), Repo.Path("installer", "linux", "DEBIAN", "postinst") })
        {
            var expression = Regex.Match(File.ReadAllText(script), "sed -i(?: '')? '(s/\"Mode\": \"Server\"/\"Mode\": \"Remote\"/)'");
            Assert.True(expression.Success, $"{script} no longer sets Mode with sed");
            if (OperatingSystem.IsWindows())
                continue;

            using var sed = Process.Start(new ProcessStartInfo("sed", [expression.Groups[1].Value, Repo.Path("appsettings.template.json")])
            {
                RedirectStandardOutput = true
            })!;
            var config = JsonDocument.Parse(sed.StandardOutput.ReadToEnd());
            sed.WaitForExit();

            Assert.Equal(0, sed.ExitCode);
            Assert.Equal("Remote", config.RootElement.GetProperty("Skanyxx").GetProperty("Mode").GetString());
        }

        var iss = File.ReadAllText(Repo.Path("installer", "windows", "skanyxx.iss"));
        Assert.Contains("StringChangeEx(Config, '\"Mode\": \"Server\"', '\"Mode\": \"Remote\"', True)", iss);
        Assert.Contains("StringChangeEx(Config, '\"RemoteUrl\": \"\"',", iss);
        // A silent install checks /REMOTEURL before any file is copied (it never sees the address page).
        Assert.Contains("Result := not WizardSilent or IsValidAddress(Param);", iss);
    }
}
