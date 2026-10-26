using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.Agents;
using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Plugins.Sources;
using static Skanyxx.Host.Tests.Infrastructure.PluginFixtures;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Git plugin sources (P9): https and a full SHA, an archive URL only for github.com and the owner's hosts, and a download
/// that cannot be pointed at the cluster's own network unless the owner allows private networks.
/// </summary>
public sealed class GitArchiveSourceTests
{
    private const string Sha = "e0fbf5e742fb4242c4ea509b395b2e343642f0b7";

    private static readonly PluginsOptions Hosts = new()
    {
        GitHosts = [new() { Host = "git.acme.example", Kind = "gitea" }, new() { Host = "gitlab.acme.example", Kind = "GitLab" }, new() { Host = "127.0.0.1:3300", Kind = "gitea" }, new() { Host = "odd.example", Kind = "svn" }]
    };

    [Theory]
    [InlineData("https://github.com/PostHog/ai-plugin", false, "https://codeload.github.com/PostHog/ai-plugin/zip/" + Sha)]
    [InlineData("https://github.com/PostHog/ai-plugin.git", false, "https://codeload.github.com/PostHog/ai-plugin/zip/" + Sha)]
    [InlineData("https://github.com/acme/.github/", false, "https://codeload.github.com/acme/.github/zip/" + Sha)]
    [InlineData("https://git.acme.example/skanyxx/plugins", false, "https://git.acme.example/skanyxx/plugins/archive/" + Sha + ".zip")]
    [InlineData("https://GITLAB.acme.example/group/sub/repo.git", false, "https://gitlab.acme.example/group/sub/repo/-/archive/" + Sha + "/repo-" + Sha + ".zip")]
    [InlineData("http://127.0.0.1:3300/skanyxx/plugins.git", true, "http://127.0.0.1:3300/skanyxx/plugins/archive/" + Sha + ".zip")]
    public void ArchiveUrls(string repo, bool allowHttp, string expected)
    {
        var settings = new PluginsOptions { GitHosts = Hosts.GitHosts, AllowHttp = allowHttp };

        Assert.Equal(expected, GitArchiveSource.ArchiveUrl(repo, Sha, settings).ToString());
    }

    [Theory]
    [InlineData("https://github.com/o/r", "e0fbf5e", "40-character")]
    [InlineData("https://github.com/o/r", "E0FBF5E742FB4242C4EA509B395B2E343642F0B7", "40-character")]
    [InlineData("https://github.com/o/r", "main", "40-character")]
    [InlineData("http://github.com/o/r", Sha, "https URL")]
    [InlineData("http://127.0.0.1:3300/o/r", Sha, "https URL")]
    [InlineData("git@github.com:o/r.git", Sha, "https URL")]
    [InlineData("file:///srv/git/o/r", Sha, "https URL")]
    [InlineData("https://token@github.com/o/r", Sha, "without credentials")]
    [InlineData("https://github.com/o/r?ref=main", Sha, "without credentials or a query")]
    [InlineData("https://github.com/o", Sha, "owner and a repository")]
    [InlineData("https://github.com/o/../r", Sha, "owner and a repository")]
    [InlineData("https://github.com/o/r%20x", Sha, "owner and a repository")]
    [InlineData("https://github.com:8443/o/r", Sha, "not github.com and not a git host")]
    [InlineData("https://github.com/o/r/tree/main", Sha, "not github.com and not a git host")]
    [InlineData("https://evil.example/o/r", Sha, "not github.com and not a git host")]
    [InlineData("https://git.acme.example/group/sub/r", Sha, "https://host/owner/repo")]
    [InlineData("https://odd.example/o/r", Sha, "must be gitea or gitlab")]
    public void BadRepositoriesAndCommits_AreRefused(string repo, string sha, string reason)
    {
        var ex = Assert.Throws<PluginSourceException>(() => GitArchiveSource.ArchiveUrl(repo, sha, Hosts));

        Assert.Contains(reason, ex.Message);
    }

    [Theory]
    [InlineData("140.82.112.3", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700::1111", true)]
    [InlineData("64:ff9b::808:808", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.10.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("100.127.255.255", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:10.1.2.3", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fc12::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("2002:c0a8:0101::1", false)]
    [InlineData("2001:db8::1", false)]
    // D221: ranges added in plugin review round 1.
    [InlineData("64:ff9b:1::808:808", false)]
    [InlineData("::ffff:0:808:808", false)]
    [InlineData("100::1", false)]
    [InlineData("192.88.99.1", false)]
    [InlineData("2001:2::1", false)]
    [InlineData("2001:4860:4860::8888", true)]
    public void OnlyPublicAddresses_AreReachable(string address, bool reachable) =>
        Assert.Equal(reachable, NetworkGuard.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task ALocalGitHost_IsRefusedByDefault_ThroughTheModulesOwnClient()
    {
        await using var server = await ArchiveServer.StartAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Plugins:AllowHttp"] = "true",
            ["Plugins:GitHosts:0:Host"] = server.Authority,
            ["Plugins:GitHosts:0:Kind"] = "gitea"
        }).Build();
        var services = new ServiceCollection();
        new AgentsModule().RegisterServices(services, config);
        await using var provider = services.BuildServiceProvider();
        var git = provider.GetRequiredService<GitArchiveSource>();

        var ex = await Assert.ThrowsAsync<PluginSourceException>(() => git.FetchAsync($"{server.Root}/o/r", Sha, TestContext.Current.CancellationToken));

        Assert.Contains("127.0.0.1", ex.Message);
        Assert.Contains("Plugins:AllowPrivateNetworks", ex.Message);
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task ALocalGitHost_IsReached_WithAllowPrivateNetworks()
    {
        await using var server = await ArchiveServer.StartAsync();

        var archive = await server.Source().FetchAsync($"{server.Root}/o/r.git", Sha, TestContext.Current.CancellationToken);

        Assert.Equal(["agents/a.md"], archive.Tree.Paths);
        Assert.Equal(archive.Tree.Digest(), archive.Digest);
    }

    [Fact]
    public async Task ARedirectOnTheSameHost_IsFollowed_ElsewhereRefused_AndCounted()
    {
        await using var server = await ArchiveServer.StartAsync();
        var ct = TestContext.Current.CancellationToken;

        Assert.Single((await server.Source().FetchAsync($"{server.Root}/hop/r", Sha, ct)).Tree.Paths);
        var away = await Assert.ThrowsAsync<PluginSourceException>(() => server.Source().FetchAsync($"{server.Root}/away/r", Sha, ct));
        Assert.Contains("only redirects on the same host", away.Message);
        var loop = await Assert.ThrowsAsync<PluginSourceException>(() => server.Source().FetchAsync($"{server.Root}/loop/r", Sha, ct));
        Assert.Contains("more than 3 times", loop.Message);
    }

    [Fact]
    public async Task ErrorsAndOversizedArchives_AreRefused()
    {
        await using var server = await ArchiveServer.StartAsync();
        var ct = TestContext.Current.CancellationToken;

        Assert.Contains("answered 404", (await Assert.ThrowsAsync<PluginSourceException>(() => server.Source().FetchAsync($"{server.Root}/none/r", Sha, ct))).Message);
        Assert.Contains("larger than", (await Assert.ThrowsAsync<PluginSourceException>(() => server.Source(maxBytes: 64).FetchAsync($"{server.Root}/o/r", Sha, ct))).Message);
    }

    [Fact]
    public async Task AHostNameResolvingToLoopback_IsRefusedAtConnect()
    {
        var ex = await Assert.ThrowsAsync<PluginSourceException>(async () =>
            await GitArchiveSource.ConnectAsync(new DnsEndPoint("localhost", 9), allowPrivate: false, TestContext.Current.CancellationToken));

        Assert.Contains("localhost resolves to", ex.Message);
    }

    /// <summary>A git host on loopback serving one archive, plus redirect routes.</summary>
    private sealed class ArchiveServer(WebApplication app, string root) : IAsyncDisposable
    {
        private int requests;

        public string Root => root;

        public string Authority => new Uri(root).Authority;

        public int Requests => requests;

        public static async Task<ArchiveServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            ArchiveServer? self = null;
            app.Use((context, next) =>
            {
                Interlocked.Increment(ref self!.requests);
                return next(context);
            });
            var zip = Zip(("r-" + Sha + "/agents/a.md", Text("---\nname: a\n---\nA.")));
            app.MapGet("/o/r/archive/{file}", () => Results.Bytes(zip, "application/zip"));
            app.MapGet("/hop/r/archive/{file}", (string file) => Results.Redirect($"/o/r/archive/{file}"));
            app.MapGet("/loop/r/archive/{file}", (string file) => Results.Redirect($"/loop/r/archive/{file}"));
            app.MapGet("/away/r/archive/{file}", (HttpContext context, string file) =>
                Results.Redirect($"http://localhost:{context.Connection.LocalPort}/o/r/archive/{file}"));
            await app.StartAsync(TestContext.Current.CancellationToken);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return self = new ArchiveServer(app, address.TrimEnd('/'));
        }

        public GitArchiveSource Source(long maxBytes = 20 * 1024 * 1024)
        {
            var settings = new PluginsOptions
            {
                AllowHttp = true,
                AllowPrivateNetworks = true,
                MaxDownloadBytes = maxBytes,
                GitHosts = [new() { Host = Authority, Kind = "gitea" }]
            };
            return new GitArchiveSource(new Clients(settings), Microsoft.Extensions.Options.Options.Create(settings));
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }

    private sealed class Clients(PluginsOptions settings) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(GitArchiveSource.Handler(settings)) { Timeout = GitArchiveSource.Timeout };
    }
}
