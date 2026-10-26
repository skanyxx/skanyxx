using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Plugins.Format;
using static Skanyxx.Host.Tests.Infrastructure.PluginFixtures;

namespace Skanyxx.Host.Tests;

/// <summary>
/// A marketplace.json is a listing (P10): folders of the same source and git repositories pinned to a full SHA install;
/// a branch or tag alone is refused as not pinned, npm/archive/command sources are unsupported, each entry on its own.
/// </summary>
public sealed class MarketplaceReaderTests
{
    private static PluginMarketplace Listing(string path = "")
    {
        var result = PluginReader.Read(Under(path), Source(path), Options);
        Assert.Null(result.Plugin);
        return result.Marketplace ?? throw new InvalidOperationException(result.Problem);
    }

    /// <summary>The marketplace fixture in folder <paramref name="path"/> of a bigger source.</summary>
    private static PluginTree Under(string path)
    {
        var marketplace = Folder("marketplace");
        return path.Length == 0 ? marketplace
            : new PluginTree(marketplace.Paths.ToDictionary(p => $"{path}/{p}", p => marketplace.Read(p)!, StringComparer.Ordinal));
    }

    private static MarketplaceEntry Entry(PluginMarketplace marketplace, string name) => marketplace.Entries.First(e => e.Name == name);

    [Fact]
    public void TheListing_HasEveryEntry_InOrder()
    {
        var marketplace = Listing();

        Assert.Equal(("acme-plugins", "Acme's plugins."), (marketplace.Name, marketplace.Description));
        Assert.Equal(18, marketplace.Entries.Count);
        Assert.Equal(new MarketplaceEntry("hello", "Says hello.", "1.0.0", "plugins/hello", null, null), Entry(marketplace, "hello"));
        Assert.Equal("plugins/rooted", Entry(marketplace, "rooted").Path);
    }

    [Fact]
    public void PinnedGitSources_AreInstallTargets()
    {
        var marketplace = Listing();

        Assert.Equal(new PinnedGitSource("https://github.com/SalesforceAIResearch/agentforce-adlc.git", "09bf1539d41f9ff355ba3eb5d05d4a75813423bb", ""), Entry(marketplace, "agentforce-adlc").Git);
        Assert.Equal(new PinnedGitSource("https://github.com/42Crunch-AI/claude-plugins.git", "faf5305385de8afed9468904e8639be737aff39e", "plugins/api-security-testing"), Entry(marketplace, "42crunch-api-security-testing").Git);
        Assert.Equal(new PinnedGitSource("https://github.com/honeycombio/agent-skill", "6bb09279fc3fb69abf113228be939dbb929e34f1", "honeycomb"), Entry(marketplace, "honeycomb").Git);
        Assert.Equal(new PinnedGitSource("https://github.com/PostHog/ai-plugin", "e0fbf5e742fb4242c4ea509b395b2e343642f0b7", ""), Entry(marketplace, "posthog").Git);
    }

    [Theory]
    [InlineData("floating", "refused", "Not pinned: 'main' can move")]
    [InlineData("unpinned", "refused", "Not pinned")]
    [InlineData("short-sha", "refused", "full 40-character")]
    [InlineData("ssh-url", "refused", "https URL")]
    [InlineData("escape", "refused", "inside the repository")]
    [InlineData("from-npm", "unsupported", "npm sources are not supported")]
    [InlineData("from-archive", "unsupported", "archive sources are not supported")]
    [InlineData("from-command", "unsupported", "command sources are not supported")]
    [InlineData("traversal", "refused", "must be ./<folder>")]
    [InlineData("missing", "refused", "not a folder of this marketplace")]
    [InlineData("bad name", "refused", "is not a plugin name")]
    public void EntriesThatCannotInstall_SayWhy(string name, string status, string reason)
    {
        var entry = Entry(Listing(), name);

        Assert.Null(entry.Path);
        Assert.Null(entry.Git);
        Assert.Equal(status, entry.Problem!.Status);
        Assert.Contains(reason, entry.Problem.Reason);
    }

    [Fact]
    public void ASecondEntryWithTheSameName_IsRefused()
    {
        var hellos = Listing().Entries.Where(e => e.Name == "hello").ToList();

        Assert.Equal(2, hellos.Count);
        Assert.Null(hellos[0].Problem);
        Assert.Contains("Listed twice", hellos[1].Problem!.Reason);
    }

    [Fact]
    public void AMarketplaceInASubfolder_GivesPathsFromTheSourceRoot_AndItsEntryReadsAsAPlugin()
    {
        var marketplace = Listing("repo/market");
        var hello = Entry(marketplace, "hello");
        Assert.Equal("repo/market/plugins/hello", hello.Path);

        var plugin = PluginReader.ReadPlugin(Under("repo/market"), Source(hello.Path!), Options, entryName: hello.Name).Plugin!;

        Assert.Equal("hello", plugin.Name);
        Assert.Equal("greeter", Assert.Single(plugin.Agents).Name);
    }

    [Theory]
    [InlineData("""{ "name": "m" }""", "no plugins list")]
    [InlineData("""{ "plugins": 3 }""", "no plugins list")]
    [InlineData("not json", "is not valid JSON")]
    public void ABrokenMarketplace_IsRefused(string json, string reason)
    {
        var result = PluginReader.Read(Tree((".claude-plugin/marketplace.json", json)), Source(), Options);

        Assert.Contains(reason, result.Problem);
    }

    [Fact]
    public void TooManyEntries_IsRefused()
    {
        var entries = string.Join(',', Enumerable.Range(0, 501).Select(i => $$"""{ "name": "p{{i}}", "source": "./p" }"""));

        var result = PluginReader.Read(Tree((".claude-plugin/marketplace.json", $$"""{ "plugins": [{{entries}}] }""")), Source(), Options);

        Assert.Contains("at most 500", result.Problem);
    }

    [Fact]
    public void HttpRepositories_OnlyWhenAllowed()
    {
        var tree = Tree((".claude-plugin/marketplace.json", """{ "plugins": [ { "name": "local", "source": { "source": "url", "url": "http://127.0.0.1:3300/o/r.git", "sha": "09bf1539d41f9ff355ba3eb5d05d4a75813423bb" } } ] }"""));

        Assert.NotNull(PluginReader.Read(tree, Source(), Options).Marketplace!.Entries[0].Problem);
        Assert.NotNull(PluginReader.Read(tree, Source(), Options with { AllowHttp = true }).Marketplace!.Entries[0].Git);
    }
}
