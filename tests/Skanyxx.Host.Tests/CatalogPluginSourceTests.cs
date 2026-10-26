using Skanyxx.Module.Agents.Plugins.Format;
using Skanyxx.Module.Agents.Plugins.Sources;
using static Skanyxx.Host.Tests.Infrastructure.PluginFixtures;

namespace Skanyxx.Host.Tests;

/// <summary>The built-in catalog (P9): embedded in the module, read by the same reader, and nothing in it is refused.</summary>
public sealed class CatalogPluginSourceTests
{
    private static readonly CatalogPluginSource Catalog = new();

    [Fact]
    public void TheCatalog_IsAMarketplaceOfTwoLocalPlugins()
    {
        var marketplace = PluginReader.Read(Catalog.Tree, Catalog.Root, Options).Marketplace!;

        Assert.Equal("skanyxx-catalog", marketplace.Name);
        Assert.Equal([("microsoft-learn", "plugins/microsoft-learn"), ("deepwiki", "plugins/deepwiki")], marketplace.Entries.Select(e => (e.Name, e.Path!)));
        Assert.All(marketplace.Entries, e => Assert.Null(e.Problem));
        // D222: a builder installs from the catalog, so nothing in it may point at third-party git content.
        Assert.All(marketplace.Entries, e => Assert.Null(e.Git));
    }

    [Theory]
    [InlineData("microsoft-learn", "plugins/microsoft-learn", "microsoft-learn-docs", "https://learn.microsoft.com/api/mcp", "docs-researcher", new[] { "microsoft_docs_search", "microsoft_docs_fetch", "microsoft_code_sample_search" })]
    [InlineData("deepwiki", "plugins/deepwiki", "deepwiki-wiki", "https://mcp.deepwiki.com/mcp", "repo-explainer", new[] { "read_wiki_structure", "read_wiki_contents", "ask_wiki_question" })]
    public void EachPlugin_TranslatesWithoutANote(string name, string path, string server, string url, string agent, string[] tools)
    {
        var plugin = PluginReader.ReadPlugin(Catalog.Tree, Catalog.Plugin(name, path), Options, name).Plugin!;

        Assert.Empty(plugin.Notes);
        var mcp = Assert.Single(plugin.Servers);
        Assert.Equal((server, "STREAMABLE_HTTP", url), (mcp.Name, mcp.Protocol, mcp.Url));
        Assert.Empty(mcp.Headers);
        var draft = Assert.Single(plugin.Agents);
        Assert.Equal(agent, draft.Name);
        Assert.InRange(draft.Instructions.Length, 1_000, 16_000);
        Assert.Equal(tools, Assert.Single(draft.McpTools).Tools);
        Assert.Equal(("catalog", name, path), (plugin.Source.Kind, plugin.Source.Location, plugin.Source.Path));
        Assert.Matches("^[0-9a-f]{64}$", plugin.Source.Digest);
    }

    [Fact]
    public void TheDigest_IsStable_AndDiffersPerPlugin()
    {
        Assert.Equal(new CatalogPluginSource().Root.Digest, Catalog.Root.Digest);
        Assert.NotEqual(Catalog.Plugin("deepwiki", "plugins/deepwiki").Digest, Catalog.Plugin("microsoft-learn", "plugins/microsoft-learn").Digest);
    }
}
