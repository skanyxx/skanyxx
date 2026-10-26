using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Plugins.Features;
using Skanyxx.Module.Agents.Plugins.Sources;
using Skanyxx.Module.Agents.Studio;
using static Skanyxx.Host.Tests.Infrastructure.PluginFixtures;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D222: a marketplace entry pinned to another git repository is third-party content; only someone who may fetch git (the
/// owner) follows it, and nothing is downloaded before that is decided.
/// </summary>
public sealed class PluginLoaderTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task AnEntryPinnedToGit_IsForbidden_ForSomeoneWhoMayNotFetchGit_BeforeAnyDownload()
    {
        var clients = new CountingClients();
        var loader = new PluginLoader(new CatalogPluginSource(), new GitArchiveSource(clients, Microsoft.Extensions.Options.Options.Create(new PluginsOptions())),
            Microsoft.Extensions.Options.Options.Create(new StudioOptions()), Microsoft.Extensions.Options.Options.Create(new PluginsOptions()));
        var zip = Zip((".claude-plugin/marketplace.json", Text($$$"""
            {"name": "m", "owner": {"name": "o"}, "plugins": [{"name": "far", "source": {"source": "github", "repo": "acme/far", "sha": "{{{Sha}}}"}}]}
            """)));

        var ex = await Assert.ThrowsAsync<PluginSourceException>(() =>
            loader.LoadAsync(new PluginSourceRequest { Source = PluginSourceKinds.Zip, Name = "far" }, new PluginUpload("m.zip", zip), mayFollowGit: false,
                TestContext.Current.CancellationToken));

        Assert.True(ex.Forbidden);
        Assert.Contains("only the owner brings in plugins from git", ex.Message);
        Assert.Equal(0, clients.Created);
    }

    private sealed class CountingClients : IHttpClientFactory
    {
        public int Created { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Created++;
            throw new InvalidOperationException("No download expected.");
        }
    }
}
