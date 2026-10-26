using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins.Format;
using static Skanyxx.Host.Tests.Infrastructure.PluginFixtures;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Claude Code plugins translated into studio drafts (P2–P5): the fixtures are real plugins' files (PostHog, Rootly,
/// Honeycomb, ArcKit, Stripe, GitHub) plus synthetic sse and stdio ones; the inline trees cover each rule and cap.
/// </summary>
public sealed class PluginReaderTests
{
    private static readonly string[] AllFixtures = ["posthog", "rootly", "honeycomb", "arckit", "stripe", "github", "events-feed", "local-tools"];

    [Fact]
    public void PostHog_WrappedMcpJson_LiteralHeader_IsASecretValue()
    {
        var plugin = Plugin("posthog");

        Assert.Equal(("posthog", "1.1.67"), (plugin.Name, plugin.Version));
        var server = Assert.Single(plugin.Servers);
        Assert.Equal(("posthog-posthog", "STREAMABLE_HTTP", "https://mcp.posthog.com/mcp"), (server.Name, server.Protocol, server.Url));
        Assert.Equal(new PluginHeader("x-posthog-mcp-consumer", null, "plugin"), Assert.Single(server.Headers));
        var agent = Assert.Single(plugin.Agents);
        Assert.Equal("error-analyzer", agent.Name);
        Assert.Equal("default-model-config", agent.ModelConfig);
        Assert.StartsWith("# PostHog Error Analyzer Agent", agent.Instructions);
        Assert.Equal("posthog-posthog", Assert.Single(agent.McpTools).Server);
        Assert.Empty(agent.McpTools[0].Tools);
        Assert.Empty(plugin.Notes);
    }

    [Fact]
    public void Agents_AreRecipesOnly_NoGrantsSkillsOrTtl()
    {
        foreach (var agent in AllFixtures.SelectMany(f => Plugin(f).Agents))
        {
            Assert.Empty(agent.Grants);
            Assert.Empty(agent.Skills);
            Assert.Null(agent.MemoryTtlDays);
            Assert.Equal("default-model-config", agent.ModelConfig);
        }
    }

    [Fact]
    public void Rootly_BuiltInsAreUnsupported_WildcardMeansAllTools_ModelIgnored()
    {
        var plugin = Plugin("rootly");

        Assert.Equal(["deploy-guardian", "incident-investigator", "retro-analyst"], plugin.Agents.Select(a => a.Name));
        var investigator = plugin.Agents.Single(a => a.Name == "incident-investigator");
        Assert.Equal(new McpToolChoice("rootly-rootly", []), Assert.Single(investigator.McpTools), ToolChoice.Comparer);
        var guardian = plugin.Agents.Single(a => a.Name == "deploy-guardian");
        Assert.Equal(["list_incidents", "get_incident"], Assert.Single(guardian.McpTools).Tools);

        const string file = "agents/incident-investigator.md";
        Assert.Contains(plugin.Notes, n => n is { Item: file, Status: "changed" } && n.Reason.Contains("model 'sonnet' ignored"));
        Assert.Contains(plugin.Notes, n => n is { Item: file, Status: "unsupported" } && n.Reason.Contains("Read, Grep, Glob, Bash"));
        Assert.DoesNotContain(plugin.Notes, n => n.Item == "agents/retro-analyst.md"); // model: inherit is not a choice
    }

    [Fact]
    public void Honeycomb_BareMcpJson_LoadedTwice_IsOneServer_BlockDescriptionIsOneLineCut()
    {
        var plugin = Plugin("honeycomb");

        Assert.Equal("honeycomb-honeycomb", Assert.Single(plugin.Servers).Name);
        var agent = Assert.Single(plugin.Agents);
        Assert.DoesNotContain('\n', agent.Description);
        Assert.InRange(agent.Description.Length, 400, 500);
        Assert.StartsWith("Use this agent to investigate production behaviour with Honeycomb: latency regressions,", agent.Description);
        Assert.Contains(plugin.Notes, n => n.Status == "changed" && n.Reason.StartsWith("Description truncated from ") && n.Reason.Contains(" to 500"));
        Assert.DoesNotContain(plugin.Notes, n => n.Status == "refused");
    }

    [Fact]
    public void ArcKit_UserConfigHeader_IsATemplate_PluginNamespacedToolMaps()
    {
        var plugin = Plugin("arckit");

        var server = Assert.Single(plugin.Servers);
        Assert.Equal("arckit-google-developer-knowledge", server.Name);
        Assert.Equal(new PluginHeader("X-Goog-Api-Key", "${user_config.GOOGLE_API_KEY}", null), Assert.Single(server.Headers));
        var agent = Assert.Single(plugin.Agents);
        Assert.Equal(["search_documents"], Assert.Single(agent.McpTools).Tools);
        Assert.Contains(plugin.Notes, n => n.Status == "unsupported" && n.Reason.Contains("WebSearch"));
    }

    [Fact]
    public void Stripe_SpacedName_IsRenamed_AllowedToolsReadAsTools()
    {
        var plugin = Plugin("stripe");

        var agent = Assert.Single(plugin.Agents);
        Assert.Equal("company-researcher", agent.Name);
        Assert.Equal(new McpToolChoice("stripe-stripe", ["search_documentation", "list_customers"]), Assert.Single(agent.McpTools), ToolChoice.Comparer);
        const string file = "agents/company-researcher.md";
        Assert.Contains(plugin.Notes, n => n is { Item: file, Status: "changed" } && n.Reason.Contains("'company-researcher' (was 'Company Researcher')"));
        Assert.Contains(plugin.Notes, n => n is { Item: file, Status: "changed" } && n.Reason.Contains("allowed-tools"));
        Assert.Contains(plugin.Notes, n => n is { Item: file, Status: "unsupported" } && n.Reason.Contains("WebFetch"));
    }

    [Fact]
    public void GitHub_NoManifest_NamedAfterTheSource_BearerVariableIsATemplate()
    {
        var plugin = Plugin("github");

        Assert.Equal("github", plugin.Name);
        Assert.Null(plugin.Version);
        var server = Assert.Single(plugin.Servers);
        Assert.Equal(("github-github", "https://api.githubcopilot.com/mcp/"), (server.Name, server.Url));
        Assert.Equal(new PluginHeader("Authorization", "Bearer ${GITHUB_PERSONAL_ACCESS_TOKEN}", null), Assert.Single(server.Headers));
        Assert.Empty(plugin.Agents);
    }

    [Fact]
    public void Sse_MapsToSse_ToolsInBothForms_MapToTheServer()
    {
        var plugin = Plugin("events-feed");

        var server = Assert.Single(plugin.Servers);
        Assert.Equal(("events-feed-feed", "SSE"), (server.Name, server.Protocol));
        Assert.Equal([new PluginHeader("X-Api-Key", null, "literal-key-123"), new PluginHeader("X-Team", "${TEAM}", null)], server.Headers);
        var agent = Assert.Single(plugin.Agents);
        Assert.Equal("feed-watcher", agent.Name);
        Assert.Equal(new McpToolChoice("events-feed-feed", ["list_events", "get_event"]), Assert.Single(agent.McpTools), ToolChoice.Comparer);
    }

    [Fact]
    public void Stdio_AndUntyped_AreUnsupported_TheRemoteOneInstalls()
    {
        var plugin = Plugin("local-tools");

        Assert.Equal("local-tools-remote", Assert.Single(plugin.Servers).Name);
        Assert.Contains(plugin.Notes, n => n is { Item: "files", Status: "unsupported" } && n.Reason.Contains("stdio"));
        Assert.Contains(plugin.Notes, n => n is { Item: "deploy-api", Status: "unsupported" } && n.Reason.Contains("stdio"));
        Assert.Contains(plugin.Notes, n => n is { Item: "untyped", Status: "unsupported" } && n.Reason.Contains("no type"));
        var agent = Assert.Single(plugin.Agents);
        Assert.Equal(new McpToolChoice("local-tools-remote", ["status"]), Assert.Single(agent.McpTools), ToolChoice.Comparer);
        Assert.Contains(plugin.Notes, n => n.Status == "unsupported" && n.Reason.Contains("'deploy-api' is not installed"));
        Assert.Contains(plugin.Notes, n => n.Status == "unsupported" && n.Reason.Contains("built-in tools") && n.Reason.Contains("Bash"));
    }

    [Fact]
    public void ALiteralHeaderValue_IsNeverATemplate()
    {
        foreach (var header in AllFixtures.SelectMany(f => Plugin(f).Servers).SelectMany(s => s.Headers))
        {
            Assert.True((header.Template is null) ^ (header.Value is null), header.Name);
            if (header.Template is not null)
                Assert.Contains("${", header.Template);
        }
        var literal = Plugin("events-feed").Servers[0].Headers[0];
        Assert.DoesNotContain("literal-key-123", literal.ToString());
    }

    [Theory]
    [InlineData("https://${HOST}/mcp", "no ${variables}")]
    [InlineData("https://api.example.com/${user_config.PATH}", "no ${variables}")]
    [InlineData("http://api.example.com/mcp", "absolute https URL")]
    [InlineData("ftp://api.example.com/mcp", "absolute https URL")]
    [InlineData("https://user:secret@api.example.com/mcp", "carries a credential")]
    [InlineData("/relative/mcp", "absolute https URL")]
    // D221: a key in the URL would go to git; an internal address is the cluster's own.
    [InlineData("https://mcp.exa.ai/mcp?exaApiKey=0123456789", "carries a credential")]
    [InlineData("https://actions.zapier.com/mcp/a8Kd93jXq0Lm2PzT7vRb4YcN6wHs1EuF/sse", "carries a credential")]
    [InlineData("https://10.0.0.5/mcp", "internal address")]
    [InlineData("https://kubernetes.default.svc/mcp", "internal address")]
    public void ABadUrl_RefusesTheServer(string url, string reason)
    {
        var plugin = Plugin(Tree((".mcp.json", $$"""{ "s": { "type": "http", "url": "{{url}}" } }""")));

        Assert.Empty(plugin.Servers);
        Assert.Contains(plugin.Notes, n => n is { Item: "s", Status: "refused" } && n.Reason.Contains(reason));
    }

    [Fact]
    public void Http_AndALoopbackServer_AreAccepted_WhenTheOwnerAllowsBoth()
    {
        var tree = Tree((".mcp.json", """{ "s": { "type": "streamable-http", "url": "http://127.0.0.1:9000/mcp" } }"""));

        Assert.Equal("STREAMABLE_HTTP", Assert.Single(Plugin(tree, Options with { AllowHttp = true, AllowPrivateServers = true }).Servers).Protocol);
        Assert.Contains(Plugin(tree, Options with { AllowHttp = true }).Notes, n => n.Reason.Contains("Plugins:AllowPrivateServers"));
    }

    // D221: Claude Code takes the last of two equal keys; reading the first would install another plugin than it runs.
    [Fact]
    public void ADuplicateJsonKey_IsRefused()
    {
        var servers = Plugin(Tree((".mcp.json", """{ "s": { "type": "http", "url": "https://a.example/mcp" }, "s": { "type": "http", "url": "https://b.example/mcp" } }""")));
        var manifest = PluginReader.Read(Tree((".claude-plugin/plugin.json", """{ "name": "one", "name": "two" }""")), Source(), Options);

        Assert.Empty(servers.Servers);
        Assert.Contains(servers.Notes, n => n is { Item: ".mcp.json", Status: "refused" } && n.Reason.Contains("not valid JSON"));
        Assert.Null(manifest.Plugin);
        Assert.Contains("not valid JSON", manifest.Problem);
    }

    [Theory]
    [InlineData("ws", "WebSocket")]
    [InlineData("sdk", "sdk")]
    [InlineData("grpc", "Unknown server type")]
    public void OtherTransports_AreUnsupported(string type, string reason)
    {
        var plugin = Plugin(Tree((".mcp.json", $$"""{ "s": { "type": "{{type}}", "url": "https://x.example/mcp" } }""")));

        Assert.Empty(plugin.Servers);
        Assert.Contains(plugin.Notes, n => n is { Item: "s", Status: "unsupported" } && n.Reason.Contains(reason));
    }

    [Theory]
    [InlineData("""{ "Bad Header": "x" }""", "not a header name Skanyxx sends")]
    [InlineData("""{ "X_Key": "x" }""", "not a header name Skanyxx sends")]
    [InlineData("""{ "Host": "evil.example" }""", "not a header name Skanyxx sends")]
    [InlineData("""{ "X-Forwarded-For": "10.0.0.1" }""", "not a header name Skanyxx sends")]
    [InlineData("""{ "X-Key": "key=${TOKEN}&x" }""", "a template is only variables")]
    [InlineData("""{ "X-Key": 42 }""", "one line of text")]
    [InlineData("""{ "X-Key": "a\nb" }""", "one line of text")]
    [InlineData("""[ "X-Key" ]""", "headers must be an object")]
    public void BadHeaders_RefuseTheServer(string headers, string reason)
    {
        var plugin = Plugin(Tree((".mcp.json", $$"""{ "s": { "type": "http", "url": "https://x.example/mcp", "headers": {{headers}} } }""")));

        Assert.Empty(plugin.Servers);
        Assert.Contains(plugin.Notes, n => n is { Item: "s", Status: "refused" } && n.Reason.Contains(reason));
    }

    [Fact]
    public void VariableDefault_IsDropped_SoNoLiteralReachesGit()
    {
        var plugin = Plugin(Tree((".mcp.json", """{ "s": { "type": "http", "url": "https://x.example/mcp", "headers": { "Authorization": "Bearer ${TOKEN:-sk-live-123}" } } }""")));

        Assert.Equal(new PluginHeader("Authorization", "Bearer ${TOKEN}", null), Assert.Single(Assert.Single(plugin.Servers).Headers));
        Assert.Contains(plugin.Notes, n => n is { Item: "s.Authorization", Status: "changed" });
        Assert.DoesNotContain(plugin.Notes, n => n.Reason.Contains("sk-live-123"));
    }

    [Fact]
    public void PluginName_IsKeptAsAStudioName_WithANote()
    {
        var plugin = Plugin(Tree((".claude-plugin/plugin.json", """{ "name": "My.Cool_Plugin" }""")));

        Assert.Equal("my-cool-plugin", plugin.Name);
        Assert.Contains(plugin.Notes, n => n is { Item: "My.Cool_Plugin", Status: "changed" });
    }

    [Fact]
    public void PluginName_StartingWithADigit_IsRefused()
    {
        var result = PluginReader.Read(Tree((".claude-plugin/plugin.json", """{ "name": "1password" }""")), Source(), Options);

        Assert.Null(result.Plugin);
        Assert.Contains("must start with a letter", result.Problem);
    }

    [Fact]
    public void OauthAndHeadersHelper_AreIgnored_WithANote()
    {
        var plugin = Plugin(Tree((".mcp.json", """{ "s": { "type": "http", "url": "https://x.example/mcp", "oauth": { "clientId": "claude" }, "headersHelper": "./auth.sh" } }""")));

        Assert.Single(plugin.Servers);
        Assert.Contains(plugin.Notes, n => n.Status == "changed" && n.Reason.StartsWith("oauth ignored"));
        Assert.Contains(plugin.Notes, n => n.Status == "changed" && n.Reason.StartsWith("headersHelper ignored"));
    }

    [Fact]
    public void ManifestMcpServers_MergeAfterMcpJson_LaterNameWins()
    {
        var plugin = Plugin(Tree(
            (".claude-plugin/plugin.json", """{ "name": "merge", "mcpServers": [ "./mcp/more.json", { "a": { "type": "sse", "url": "https://inline.example/sse" } } ] }"""),
            (".mcp.json", """{ "mcpServers": { "a": { "type": "http", "url": "https://first.example/mcp" }, "b": { "type": "http", "url": "https://b.example/mcp" } } }"""),
            ("mcp/more.json", """{ "c": { "type": "http", "url": "https://c.example/mcp" } }""")));

        Assert.Equal(["merge-a", "merge-b", "merge-c"], plugin.Servers.Select(s => s.Name));
        Assert.Equal(("SSE", "https://inline.example/sse"), (plugin.Servers[0].Protocol, plugin.Servers[0].Url));
    }

    [Theory]
    [InlineData("\"mcp/more.json\"", "refused")]
    [InlineData("\"./../more.json\"", "refused")]
    [InlineData("\"./mcp/more.txt\"", "refused")]
    [InlineData("\"./server.mcpb\"", "unsupported")]
    [InlineData("\"https://example.com/server.mcpb\"", "unsupported")]
    [InlineData("42", "refused")]
    public void ManifestMcpPaths_FollowClaudeCodesRule(string entry, string status)
    {
        var plugin = Plugin(Tree(
            (".claude-plugin/plugin.json", $$"""{ "name": "paths", "mcpServers": {{entry}} }"""),
            ("mcp/more.json", """{ "c": { "type": "http", "url": "https://c.example/mcp" } }""")));

        Assert.Empty(plugin.Servers);
        Assert.Contains(plugin.Notes, n => n.Status == status);
    }

    [Fact]
    public void ManifestAgents_ReplaceTheDefaultFolder()
    {
        var plugin = Plugin(Tree(
            (".claude-plugin/plugin.json", """{ "name": "custom", "agents": ["./custom/one.md", "custom/two.md", "./../three.md", "./agents", "./custom/missing.md"] }"""),
            ("custom/one.md", "---\nname: one\ndescription: One.\n---\nDo one."),
            ("custom/two.md", "---\nname: two\ndescription: Two.\n---\nDo two."),
            ("agents/default.md", "---\nname: default\ndescription: Default.\n---\nDo default.")));

        Assert.Equal("one", Assert.Single(plugin.Agents).Name);
        Assert.Equal(["custom/two.md", "./../three.md", "./agents", "./custom/missing.md"], plugin.Notes.Where(n => n.Status == "refused").Select(n => n.Item));
    }

    [Fact]
    public void DefaultFolder_IsScannedForMarkdownOnly_SubfoldersIncluded()
    {
        var plugin = Plugin(Tree(
            ("agents/review/security.md", "Check security."),
            ("agents/notes.txt", "not an agent"),
            ("agents/readme.MD", "upper-case extension is not .md")));

        Assert.Equal("review-security", Assert.Single(plugin.Agents).Name);
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/review/security.md", Status: "changed" } && n.Reason.Contains("No YAML frontmatter"));
    }

    [Theory]
    [InlineData("---\nname: [unclosed\n---\nBody.", "Frontmatter ignored")]
    [InlineData("---\nname: x\nname: y\n---\nBody.", "Frontmatter ignored")]
    [InlineData("---\nbase: &a x\nname: *a\n---\nBody.", "Frontmatter ignored")]
    [InlineData("---\n- a list\n---\nBody.", "Frontmatter ignored")]
    [InlineData("---\nname: never-closed\nBody.", "no closing ---")]
    public void BrokenFrontmatter_NamesTheAgentAfterTheFile(string text, string reason)
    {
        var plugin = Plugin(Tree(("agents/fallback.md", text)));

        var agent = Assert.Single(plugin.Agents);
        Assert.Equal("fallback", agent.Name);
        Assert.Equal("Agent from the inline plugin", agent.Description);
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/fallback.md", Status: "changed" } && n.Reason.Contains(reason));
    }

    [Theory]
    [InlineData("seed", "reserved")]
    [InlineData("skanyxx-helper", "reserved")]
    [InlineData("preview-1-x", "reserved")]
    [InlineData("1st-agent", "does not make an agent name")]
    [InlineData("שלום", "does not make an agent name")]
    public void UnusableNames_RefuseTheAgent(string name, string reason)
    {
        var plugin = Plugin(Tree(("agents/a.md", $"---\nname: {name}\ndescription: D.\n---\nBody.")));

        Assert.Empty(plugin.Agents);
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/a.md", Status: "refused" } && n.Reason.Contains(reason));
    }

    [Fact]
    public void LongNames_AreCutTo33_AndNoted()
    {
        var plugin = Plugin(Tree(("agents/a.md", "---\nname: A_Very Long Agent Name That Goes On And On\ndescription: D.\n---\nBody.")));

        var agent = Assert.Single(plugin.Agents);
        Assert.Equal("a-very-long-agent-name-that-goes", agent.Name);
        Assert.True(agent.Name.Length <= 33);
        Assert.Contains(plugin.Notes, n => n.Status == "changed" && n.Reason.Contains("(was 'A_Very Long Agent Name That Goes On And On')"));
    }

    [Fact]
    public void DuplicateAgentNames_AfterSanitising_AreRefused()
    {
        var plugin = Plugin(Tree(
            ("agents/a.md", "---\nname: Helper\ndescription: D.\n---\nBody."),
            ("agents/b.md", "---\nname: helper\ndescription: D.\n---\nBody.")));

        Assert.Single(plugin.Agents);
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/b.md", Status: "refused" } && n.Reason.Contains("already named 'helper'"));
    }

    [Fact]
    public void Instructions_AreRequired_AndNeverCut()
    {
        var exact = new string('x', 16_000);
        var plugin = Plugin(Tree(
            ("agents/empty.md", "---\nname: empty\ndescription: D.\n---\n  \n"),
            ("agents/exact.md", $"---\nname: exact\ndescription: D.\n---\n{exact}"),
            ("agents/long.md", $"---\nname: long\ndescription: D.\n---\n{exact}y"),
            ("agents/hidden.md", "---\nname: hidden\ndescription: D.\n---\nDo this‮and not that."),
            ("agents/windows.md", "---\r\nname: windows\r\ndescription: D.\r\n---\r\nLine one.\r\nLine two.\r\n")));

        Assert.Equal(["exact", "windows"], plugin.Agents.Select(a => a.Name));
        Assert.Equal(exact, plugin.Agents[0].Instructions);
        Assert.Equal("Line one.\nLine two.", plugin.Agents[1].Instructions);
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/empty.md", Status: "refused" } && n.Reason.StartsWith("No instructions"));
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/long.md", Status: "refused" } && n.Reason.Contains("16001 characters"));
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/hidden.md", Status: "refused" } && n.Reason.Contains("invisible"));
    }

    [Fact]
    public void Description_IsOneLine_WithoutInvisibleCharacters()
    {
        var plugin = Plugin(Tree(("agents/a.md", "---\nname: a\ndescription: \"Finds\\tbugs\\u202E\\n  fast.\"\n---\nBody.")));

        Assert.Equal("Finds bugs fast.", Assert.Single(plugin.Agents).Description);
    }

    [Theory]
    [InlineData("mcp__s__get_*", "name one tool")]
    [InlineData("mcp__other__tool", "'other' is not an MCP server of this plugin")]
    [InlineData("mcp__plugin_someone-else_s__tool", "is not an MCP server of this plugin")]
    public void ToolsThatCannotMap_AreUnsupported(string tool, string reason)
    {
        var plugin = Plugin(Tree(
            (".mcp.json", """{ "s": { "type": "http", "url": "https://s.example/mcp" } }"""),
            ("agents/a.md", $"---\nname: a\ndescription: D.\ntools: {tool}\n---\nBody.")));

        Assert.Empty(Assert.Single(plugin.Agents).McpTools);
        Assert.Contains(plugin.Notes, n => n.Status == "unsupported" && n.Reason.Contains(reason));
    }

    [Fact]
    public void AServerOnlyToolEntry_MeansAllTools_AndWinsOverSingleTools()
    {
        var plugin = Plugin(Tree(
            (".mcp.json", """{ "s": { "type": "http", "url": "https://s.example/mcp" }, "t": { "type": "http", "url": "https://t.example/mcp" } }"""),
            ("agents/a.md", "---\nname: a\ndescription: D.\ntools: mcp__t__one, mcp__s__one, mcp__s\n---\nBody.")));

        Assert.Equal([new McpToolChoice("inline-s", []), new McpToolChoice("inline-t", ["one"])], Assert.Single(plugin.Agents).McpTools, ToolChoice.Comparer);
    }

    [Fact]
    public void Caps_AtMostTwentyAgents_AndTenServers()
    {
        var servers = string.Join(", ", Enumerable.Range(1, 11).Select(i => $$"""
            "s{{i:00}}": { "type": "http", "url": "https://s{{i}}.example/mcp" }
            """));
        var files = Enumerable.Range(1, 21).Select(i => ($"agents/a{i:00}.md", $"---\nname: a{i:00}\ndescription: D.\n---\nBody."))
            .Append((".mcp.json", $"{{ {servers} }}")).ToArray();

        var plugin = Plugin(Tree(files));

        Assert.Equal(20, plugin.Agents.Count);
        Assert.Equal(10, plugin.Servers.Count);
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/a21.md", Status: "refused" } && n.Reason.Contains("at most 20 agents"));
        Assert.Contains(plugin.Notes, n => n is { Item: "s11", Status: "refused" } && n.Reason.Contains("at most 10 MCP servers"));
        Assert.All(plugin.Agents, a => Assert.Equal(10, a.McpTools.Count));
    }

    [Fact]
    public void FilesOver256KB_AreRefused()
    {
        var plugin = Plugin(Tree(("agents/big.md", "---\nname: big\n---\n" + new string('x', 256 * 1024))));

        Assert.Empty(plugin.Agents);
        Assert.Contains(plugin.Notes, n => n is { Item: "agents/big.md", Status: "refused" } && n.Reason.Contains("larger than 256 KB"));
    }

    [Theory]
    [InlineData("Weird_Plugin", "s.Name", "weird-plugin-s-name", true)]
    [InlineData("x", "a-very-long-server-name-that-keeps-going-and-going-and-going", "x-a-very-long-server-name-that-keeps-going-and-goi", true)]
    [InlineData("skanyxx", "memory", null, false)]
    [InlineData("preview", "x", null, false)]
    public void ServerNames_AreKubeNames_NotTheStudios(string plugin, string server, string? expected, bool installs)
    {
        var read = Plugin(Tree(
            (".claude-plugin/plugin.json", $$"""{ "name": "{{plugin}}" }"""),
            (".mcp.json", $$"""{ "{{server}}": { "type": "http", "url": "https://s.example/mcp" } }""")));

        if (installs)
        {
            var name = Assert.Single(read.Servers).Name;
            Assert.Equal(expected, name);
            Assert.True(name.Length <= 50);
            Assert.Contains(read.Notes, n => n.Item == server && n.Status == "changed");
        }
        else
            Assert.Contains(read.Notes, n => n is { Status: "refused" } && n.Reason.Contains("skanyxx- or preview-"));
    }

    [Fact]
    public void ServersThatSanitiseToTheSameName_TheSecondIsRefused()
    {
        var plugin = Plugin(Tree((".mcp.json", """{ "Api": { "type": "http", "url": "https://a.example/mcp" }, "api": { "type": "http", "url": "https://b.example/mcp" } }""")));

        Assert.Equal("https://a.example/mcp", Assert.Single(plugin.Servers).Url);
        Assert.Contains(plugin.Notes, n => n is { Item: "api", Status: "refused" } && n.Reason.Contains("already named"));
    }

    [Theory]
    [InlineData("""{ "name": "has space" }""", "is not a plugin name")]
    [InlineData("""{ "name": "-dash" }""", "is not a plugin name")]
    [InlineData("""{ "name": "x", """, "is not valid JSON")]
    [InlineData("""[ "not", "an", "object" ]""", "must be a JSON object")]
    public void ABadManifest_RefusesThePlugin(string manifest, string reason)
    {
        var result = PluginReader.Read(Tree((".claude-plugin/plugin.json", manifest)), Source(), Options);

        Assert.Null(result.Plugin);
        Assert.Contains(reason, result.Problem);
    }

    [Fact]
    public void DeepJson_IsRefused()
    {
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "1" + new string('}', 40);
        var result = PluginReader.Read(Tree((".claude-plugin/plugin.json", deep)), Source(), Options);

        Assert.Contains("is not valid JSON", result.Problem);
    }

    [Fact]
    public void UnknownKeysAreIgnored_ClaudeOnlyComponentsAreReported()
    {
        var plugin = Plugin(Tree(
            (".claude-plugin/plugin.json", """{ "name": "extras", "displayName": "Extras", "futureKey": { "x": 1 }, "commands": "./cmds" }"""),
            ("skills/review/SKILL.md", "skill"),
            ("hooks/hooks.json", "{}"),
            ("agents/a.md", "---\nname: a\ndescription: D.\nskills: [review]\nmodel: opus\n---\nBody.")));

        Assert.Equal(["skills", "commands", "hooks"], plugin.Notes.Where(n => n.Status == "unsupported").Select(n => n.Item));
        Assert.Contains(plugin.Notes, n => n.Status == "changed" && n.Reason.StartsWith("skills ignored"));
        Assert.Contains(plugin.Notes, n => n.Status == "changed" && n.Reason.StartsWith("model 'opus' ignored: plugin agents run on default-model-config"));
    }

    [Fact]
    public void NoTools_AndNoServers_GivesAPlainAgent()
    {
        var plugin = Plugin(Tree(("agents/a.md", "---\nname: a\ndescription: D.\n---\nBody.")));

        Assert.Empty(Assert.Single(plugin.Agents).McpTools);
        Assert.Empty(plugin.Notes);
    }

    [Fact]
    public void AMissingFolder_IsRefused()
    {
        var result = PluginReader.ReadPlugin(Tree(("a/x.md", "x")), Source("b"), Options);

        Assert.Contains("no folder 'b'", result.Problem);
    }

    private sealed class ToolChoice : IEqualityComparer<McpToolChoice>
    {
        public static readonly ToolChoice Comparer = new();

        public bool Equals(McpToolChoice? x, McpToolChoice? y) => x?.Server == y?.Server && (x?.Tools ?? []).SequenceEqual(y?.Tools ?? []);

        public int GetHashCode(McpToolChoice obj) => obj.Server.GetHashCode();
    }
}
