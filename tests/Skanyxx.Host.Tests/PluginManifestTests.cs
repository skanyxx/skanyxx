using System.Text.Json;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Plugins;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Host.Tests;

/// <summary>
/// A plugin's files (P1, P3, P7): <c>plugin.yaml</c> and <c>mcp/&lt;server&gt;.yaml</c> read back to exactly what they
/// were rendered from, anything else (an inline header value, a ConfigMap, TLS, a literal in a template) is not ours, and
/// an agent may use a plugin server with no tool names (every tool) while an allow-listed server still needs them.
/// </summary>
public sealed class PluginManifestTests
{
    private static readonly StudioOptions Options = new() { McpServers = ["jira"] };

    private static readonly PluginsOptions Plugins = new();

    private static PluginServerDraft Server(string url = "https://mcp.example.com/mcp", params PluginHeader[] headers) =>
        new("docs-search", "Searches the docs", "STREAMABLE_HTTP", url, headers);

    private static InstalledPlugin Plugin(string? version = "1.2.0") => new(
        "docs", version, "Documentation helpers",
        new PluginSource("git", "https://github.com/acme/plugins", new string('a', 40), "plugins/docs", new string('b', 64)),
        ["doc-helper"], ["docs-search"],
        [new PluginHeaderTemplate("docs-search", "Authorization", "Bearer ${DOCS_TOKEN}"), new PluginHeaderTemplate("docs-search", "X-Api-Key", null)],
        [new PluginNote("agents/reviewer.md", "unsupported", "tool Bash has no kagent equivalent")]);

    [Fact]
    public void AServer_ReadsBack_AsTheSameServer_WithItsHeadersFromItsOwnSecret()
    {
        var server = Server(headers: [new PluginHeader("Authorization", "Bearer ${T}", null), new PluginHeader("X-Api-Key", null, "literal-value-123")]);

        var yaml = McpServerManifest.Render(server, "kagent");
        var (read, problem) = McpServerManifest.Read("docs-search", yaml, "kagent");

        Assert.Null(problem);
        Assert.Equal(["Authorization", "X-Api-Key"], read!.Headers.Select(h => h.Name));
        Assert.All(read.Headers, h => Assert.Null(h.Value));
        Assert.DoesNotContain("literal-value-123", yaml);
        Assert.DoesNotContain("${T}", yaml);
        Assert.Contains("key: x-api-key", yaml);
        Assert.Contains("name: docs-search\n", yaml);
        Assert.Empty(PluginRules.CheckServer(read, Options, Plugins));
    }

    public static TheoryData<string, string> NotOurServer => new()
    {
        { "an inline header value", "      key: authorization\n|      key: authorization\n    value: Bearer abc\n" },
        { "a ConfigMap", "type: Secret|type: ConfigMap" },
        { "another Secret", "name: docs-search\n      key|name: someone-elses\n      key" },
        { "TLS settings", "  protocol: STREAMABLE_HTTP\n|  protocol: STREAMABLE_HTTP\n  tls:\n    disableVerify: \"true\"\n" },
        { "allowed namespaces", "  protocol: STREAMABLE_HTTP\n|  protocol: STREAMABLE_HTTP\n  allowedNamespaces:\n    from: All\n" },
        { "another kind", "kind: RemoteMCPServer|kind: MCPServer" },
        { "another namespace", "  namespace: kagent\n|  namespace: kube-system\n" },
        { "another name", "  name: docs-search\n|  name: other\n" },
        { "a key that is not the header's", "key: authorization|key: token" },
        { "an anchor", "  protocol: STREAMABLE_HTTP\n|  protocol: &p STREAMABLE_HTTP\n" }
    };

    [Theory]
    [MemberData(nameof(NotOurServer))]
    public void AServerFile_OfAnyOtherShape_IsNotOurs(string what, string edit)
    {
        var yaml = McpServerManifest.Render(Server(headers: [new PluginHeader("Authorization", "Bearer ${T}", null)]), "kagent");
        var (from, to) = (edit.Split('|')[0], edit.Split('|')[1]);
        Assert.Contains(from, yaml);

        var (read, problem) = McpServerManifest.Read("docs-search", yaml.Replace(from, to), "kagent");

        Assert.True(read is null, what);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("http://mcp.example.com/mcp", false)]
    [InlineData("https://user:pw@mcp.example.com/mcp", false)]
    [InlineData("https://mcp.example.com/${TENANT}/mcp", false)]
    [InlineData("ftp://mcp.example.com", false)]
    [InlineData("not a url", false)]
    [InlineData("https://mcp.example.com/mcp", true)]
    public void AServerUrl_IsHttpsWithoutCredentialsOrVariables(string url, bool valid) =>
        Assert.Equal(valid, PluginRules.CheckServer(Server(url), Options, Plugins).Count == 0);

    [Fact]
    public void Http_AndInternalAddresses_AreTheOwnersSwitches()
    {
        var url = Server("http://mcp.internal:8080/mcp");

        Assert.Contains(PluginRules.CheckServer(url, Options, new PluginsOptions { AllowHttp = true }), p => p.Contains("Plugins:AllowPrivateServers"));
        Assert.Empty(PluginRules.CheckServer(url, Options, new PluginsOptions { AllowHttp = true, AllowPrivateServers = true }));
        Assert.Empty(PluginRules.CheckServer(Server("http://mcp.example.com/mcp"), Options, new PluginsOptions { AllowHttp = true }));
        // D223: Plugins:* is read in one place, one type per file.
        Assert.Null(typeof(StudioOptions).GetProperty("PluginAllowHttp"));
        Assert.Empty(typeof(PluginsOptions).GetNestedTypes());
    }

    // D221: a credential in the URL goes to git as it is; it belongs in a header.
    [Theory]
    [InlineData("https://mcp.exa.ai/mcp?exaApiKey=0123456789abcdef", false)]
    [InlineData("https://server.smithery.ai/x/mcp?api_key=k&profile=p", false)]
    [InlineData("https://mcp.example.com/mcp?access_token=abc", false)]
    [InlineData("https://mcp.example.com/mcp?Signature=abc", false)]
    [InlineData("https://mcp.example.com/mcp?session=abc", false)]
    [InlineData("https://actions.zapier.com/mcp/a8Kd93jXq0Lm2PzT7vRb4YcN6wHs1EuF/sse", false)]
    [InlineData("https://mcp.example.com/sk-live-0123456789abcdefghij/mcp", false)]
    [InlineData("https://mcp.example.com/mcp?apiKey=abc", false)]
    [InlineData("https://mcp.example.com/mcp?APIKey=abc", false)]
    [InlineData("https://mcp.example.com/mcp?apikey=abc", false)]
    [InlineData("https://mcp.example.com/mcp?access-token=abc", false)]
    [InlineData("https://mcp.example.com/mcp?X-Sig=abc", false)]
    [InlineData("https://mcp.example.com/mcp?code=abc", false)]
    [InlineData("https://mcp.example.com/mcp#token=abc", false)]
    [InlineData("https://mcp.example.com/mcp#", false)]
    // Lowercase compounds ending in a credential word, and a random value under any key (sec3 R3).
    [InlineData("https://mcp.example.com/mcp?authtoken=abc", false)]
    [InlineData("https://mcp.example.com/mcp?accesskey=abc", false)]
    [InlineData("https://mcp.example.com/mcp?clientsecret=abc", false)]
    [InlineData("https://mcp.example.com/mcp?privatekey=abc", false)]
    [InlineData("https://mcp.example.com/mcp?apitoken=abc", false)]
    [InlineData("https://mcp.example.com/mcp?sessiontoken=abc", false)]
    [InlineData("https://mcp.example.com/mcp?subscriptionkey=abc", false)]
    [InlineData("https://mcp.example.com/mcp?userpassword=abc", false)]
    [InlineData("https://mcp.example.com/mcp?q=a8Kd93jXq0Lm2PzT7vRb4YcN6wHs1EuF", false)]
    [InlineData("https://mcp.example.com/mcp?q=a8Kd93jXq0Lm2PzT%37vRb4YcN6wHs1EuF", false)]
    [InlineData("https://mcp.example.com/mcp?q=search-for-a-perfectly-readable-long-phrase", true)]
    [InlineData("https://mcp.example.com/mcp?author=ann&design=flat&barcode=1&encoding=utf8", true)]
    [InlineData("https://mcp.example.com/mcp?tenant=acme&region=eu", true)]
    [InlineData("https://mcp.example.com/a-perfectly-readable-very-long-path-name/mcp", true)]
    [InlineData("https://learn.microsoft.com/api/mcp", true)]
    public void AServerUrl_CarryingACredential_IsRefused(string url, bool valid)
    {
        var problems = PluginRules.CheckServer(Server(url), Options, Plugins);

        Assert.Equal(valid, problems.Count == 0);
        if (!valid)
            Assert.Contains(problems, p => p.Contains("carries a credential"));
    }

    // D221: kagent connects from inside the cluster, with the owner's header values.
    [Theory]
    [InlineData("https://10.0.0.5/mcp")]
    [InlineData("https://[fd00::1]/mcp")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://2130706433/mcp")]
    [InlineData("https://localhost:8443/mcp")]
    [InlineData("https://gitea/mcp")]
    [InlineData("https://kubernetes.default.svc/api")]
    [InlineData("https://gitea.skanyxx.svc.cluster.local/mcp")]
    [InlineData("https://printer.local/mcp")]
    [InlineData("https://metadata.google.internal/computeMetadata")]
    [InlineData("https://app.localhost/mcp")]
    [InlineData("https://10.0.0.5./mcp")]
    [InlineData("https://127.0.0.1./mcp")]
    [InlineData("https://gitea.skanyxx.svc./mcp")]
    [InlineData("https://mcp.example.123/mcp")]
    [InlineData("https://mcp.example.x/mcp")]
    public void AServerUrl_NamingAnInternalAddress_IsRefused_UnlessTheOwnerAllowsIt(string url)
    {
        Assert.Contains(PluginRules.CheckServer(Server(url), Options, Plugins), p => p.Contains("internal address"));
        Assert.Empty(PluginRules.CheckServer(Server(url), Options, new PluginsOptions { AllowPrivateServers = true }));
    }

    // An IDN top-level domain in its xn-- form is a public name (cr3#6): .рф, .рус.
    [Theory]
    [InlineData("https://xn--d1acufc.xn--p1ai/mcp")]
    [InlineData("https://mcp.xn--p1acf/mcp")]
    [InlineData("https://xn--bcher-kva.example/mcp")]
    public void AServerUrl_UnderAnIdnTopLevelDomain_IsPublic(string url) =>
        Assert.Empty(PluginRules.CheckServer(Server(url), Options, Plugins));

    // .NET collapses dot segments, kagent sends the path as written (sec3 R2): the address checked must be the one asked for.
    [Theory]
    [InlineData("https://server.smithery.ai/@attacker/x/../../@vendor/x/mcp")]
    [InlineData("https://server.smithery.ai/@vendor/./x/mcp")]
    [InlineData("https://server.smithery.ai/@attacker/x/%2e%2e/%2E%2E/@vendor/x/mcp")]
    [InlineData("https://server.smithery.ai/@attacker/x/.%2e/@vendor/mcp")]
    [InlineData("https://server.smithery.ai/@attacker/x/%2E./@vendor/mcp")]
    [InlineData("https://server.smithery.ai/x/%2e/mcp")]
    [InlineData("https://server.smithery.ai/x/..")]
    [InlineData("https://server.smithery.ai/x/..?profile=p")]
    public void AServerUrl_WithADotSegment_IsRefused(string url) =>
        Assert.Contains(PluginRules.CheckServer(Server(url), Options, new PluginsOptions { AllowPrivateServers = true }), p => p.Contains("'..' segment"));

    [Theory]
    [InlineData("https://server.smithery.ai/v1.2/mcp")]
    [InlineData("https://server.smithery.ai/.well-known/mcp")]
    [InlineData("https://server.smithery.ai/x/...")]
    [InlineData("https://server.smithery.ai/mcp?path=../x")]
    public void AServerUrl_WithDotsThatAreNotASegment_IsAccepted(string url) =>
        Assert.Empty(PluginRules.CheckServer(Server(url), Options, Plugins));

    // .NET keeps a full-width or other non-ASCII host unmapped, while kagent's Go client maps it to 127.0.0.1 or a cluster name.
    [Theory]
    [InlineData("https://\uFF11\uFF12\uFF17.0.0.1/mcp")]
    [InlineData("https://gitea.\uFF53\uFF4B\uFF41\uFF4E\uFF59\uFF58\uFF58.\uFF53\uFF56\uFF43/mcp")]
    [InlineData("https://b\u00FCcher.example/mcp")]
    public void AServerUrl_WithANonAsciiHost_IsRefused_EvenWhenTheOwnerAllowsPrivateServers(string url)
    {
        Assert.Contains(PluginRules.CheckServer(Server(url), Options, Plugins), p => p.Contains("plain ASCII"));
        Assert.Contains(PluginRules.CheckServer(Server(url), Options, new PluginsOptions { AllowPrivateServers = true }), p => p.Contains("plain ASCII"));
        Assert.Empty(PluginRules.CheckServer(Server("https://xn--bcher-kva.example/mcp"), Options, Plugins));
        Assert.Empty(PluginRules.CheckServer(Server("https://mcp.example.com/b\u00FCcher"), Options, Plugins));
    }

    // Values are bound to the server's whole address but its query: multi-tenant hosts tell tenants apart by path.
    [Fact]
    public void TheAddress_ValuesAreBoundTo_IsSchemeHostPortAndPath()
    {
        Assert.Equal("https://server.smithery.ai/@vendor/x/mcp", PluginServerUrl.Address("https://SERVER.Smithery.ai:443/@vendor/x/mcp?profile=p"));
        Assert.NotEqual(PluginServerUrl.Address("https://server.smithery.ai/@vendor/x/mcp"), PluginServerUrl.Address("https://server.smithery.ai/@attacker/x/mcp"));
        Assert.Equal("https://mcp.example.com:8443/MCP", PluginServerUrl.Address("https://mcp.example.com:8443/MCP"));
    }

    // sec3 R1: a server is a plugin's own only when its file is in that plugin's folder, whatever its manifest lists.
    [Fact]
    public void AnAgentsOwnServers_AreOnlyThoseFiledInItsPluginsFolder()
    {
        var docs = Plugin() with { Servers = ["docs-search", "gh-tools"] };
        var tree = new RepoPlugins(new Dictionary<string, InstalledPlugin> { ["docs"] = docs }, new Dictionary<string, string>(),
            new Dictionary<string, PluginServerEntry>(), new Dictionary<string, string>(),
            new Dictionary<string, string> { ["docs-search"] = "docs", ["gh-tools"] = "github" }, new HashSet<string>());

        Assert.Equal(["docs-search"], StudioReconciler.OwnServers(tree, "doc-helper"));
        Assert.Empty(StudioReconciler.OwnServers(tree, "someone-else"));
    }

    // sec3 R4: Skanyxx cannot fence kagent's controller itself, so the switches that widen its reach are said at startup.
    [Fact]
    public void TheSwitchesThatWidenTheControllersReach_AreWarnedOutsideDevelopment()
    {
        var both = new PluginsOptions { AllowHttp = true, AllowPrivateServers = true };

        var warnings = PluginEgressWarnings.Of(new HostingEnvironment { EnvironmentName = Environments.Production }, both).ToList();

        Assert.Equal(2, warnings.Count);
        Assert.StartsWith("Plugins:AllowHttp is on outside Development", warnings[0]);
        Assert.StartsWith("Plugins:AllowPrivateServers is on outside Development", warnings[1]);
        Assert.All(warnings, w => Assert.Contains("networkPolicy.controllerEgress", w));
        Assert.Empty(PluginEgressWarnings.Of(new HostingEnvironment { EnvironmentName = Environments.Development }, both));
        Assert.Empty(PluginEgressWarnings.Of(new HostingEnvironment { EnvironmentName = Environments.Production }, new PluginsOptions()));
    }

    // D221: a plugin does not steer the connection, a proxy or what the far end believes about the caller.
    [Theory]
    [InlineData("Host")]
    [InlineData("cookie")]
    [InlineData("Set-Cookie")]
    [InlineData("Content-Length")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Connection")]
    [InlineData("Proxy-Authorization")]
    [InlineData("X-Forwarded-For")]
    [InlineData("Forwarded")]
    [InlineData("TE")]
    [InlineData("Upgrade")]
    [InlineData("X-Real-IP")]
    [InlineData("True-Client-IP")]
    [InlineData("X-Client-IP")]
    [InlineData("CF-Connecting-IP")]
    [InlineData("X-Original-Forwarded-For")]
    [InlineData("X-Original-URL")]
    [InlineData("X-Rewrite-URL")]
    [InlineData("X-Host")]
    [InlineData("X-HTTP-Method-Override")]
    [InlineData("X-Original-Host")]
    [InlineData("X-Envoy-Original-Path")]
    [InlineData("x-envoy-internal")]
    public void ASensitiveHeaderName_IsRefused(string name)
    {
        Assert.False(PluginRules.IsHeaderName(name));
        Assert.Contains(PluginRules.CheckServer(Server(headers: [new PluginHeader(name, "${T}", null)]), Options, Plugins), p => p.Contains("headers"));
        Assert.True(PluginRules.IsHeaderName("X-Api-Key"));
        Assert.True(PluginRules.IsHeaderName("Authorization"));
    }

    // D219: a third party's server never sits beside company memory, and never with "every tool, now and later".
    [Fact]
    public void AnAgentOutsideThePlugin_NamesItsToolsOnAPluginServer_AndHasNoMemoryWithIt()
    {
        var plugin = new Dictionary<string, string> { ["docs-search"] = "docs" };
        var agent = new AgentDraft("uses-docs", "d", "default-model-config", "x", [], [new McpToolChoice("docs-search", ["search_docs"])], [], null);

        Assert.Empty(DraftRules.Check(agent, Options, plugin));
        Assert.Contains(DraftRules.Check(agent with { McpTools = [new McpToolChoice("docs-search", [])] }, Options, plugin), p => p.Contains("name at least one tool"));
        Assert.Contains(DraftRules.Check(agent with { Grants = [new StudioGrant("company", true, false)] }, Options, plugin), p => p.Contains("third-party"));
        Assert.Contains(DraftRules.Check(agent with { MemoryTtlDays = 7 }, Options, plugin), p => p.Contains("third-party"));
        // Memory with an allow-listed server stays allowed.
        Assert.Empty(DraftRules.Check(agent with { McpTools = [new McpToolChoice("jira", ["search"])], Grants = [new StudioGrant("company", true, false)] }, Options, plugin));
        // The plugin's own agent keeps "every tool" of its own servers.
        Assert.Empty(DraftRules.Check(agent with { McpTools = [new McpToolChoice("docs-search", [])] }, Options, plugin, ["docs-search"]));
    }

    // D219: an agent no plugin owns never lets one party's content steer another party's credential or the owner's tools.
    [Fact]
    public void AnAgentOutsideThePlugins_UsesServersOfOnePlugin_AndNeverBesideAnAllowListedServer()
    {
        var plugins = new Dictionary<string, string> { ["docs-search"] = "docs", ["docs-index"] = "docs", ["gh-tools"] = "github" };
        var agent = new AgentDraft("uses-docs", "d", "default-model-config", "x", [],
            [new McpToolChoice("docs-search", ["search_docs"]), new McpToolChoice("docs-index", ["index"])], [], null);

        Assert.Empty(DraftRules.Check(agent, Options, plugins));
        Assert.Contains(DraftRules.Check(agent with { McpTools = [.. agent.McpTools, new McpToolChoice("gh-tools", ["get_file_contents"])] }, Options, plugins),
            p => p.Contains("belong to different plugins"));
        Assert.Contains(DraftRules.Check(agent with { McpTools = [.. agent.McpTools, new McpToolChoice("jira", ["search"])] }, Options, plugins),
            p => p.Contains("beside an allow-listed server (jira)"));
        // A plugin's own agent is held to its recipe instead (only its servers).
        Assert.Empty(DraftRules.Check(agent with { McpTools = [new McpToolChoice("docs-search", [])] }, Options, plugins, ["docs-search", "docs-index"]));
    }

    // D222: plugin text in the PR body renders as text, never as a link or an image.
    [Fact]
    public void ThePluginReport_InThePullRequest_IsInACodeBlock_ThatItsTextCannotClose()
    {
        var body = ProposalTrailer.WritePlugin("docs", "owner-1", "Docs ![x](https://evil.example/p.png)", ["- a: changed (```` [y](https://evil.example))"]);

        Assert.StartsWith("Plugin: docs\nProposed-by: owner-1\n\n`````text\nDocs ![x]", body);
        Assert.Contains("\n`````\n\nProposed in Skanyxx", body);
        Assert.Equal("docs", ProposalTrailer.PluginOf(body));
        Assert.Equal("owner-1", ProposalTrailer.ProposedBy(body));
    }

    [Theory]
    [InlineData("jira")]
    [InlineData("skanyxx-memory-x")]
    [InlineData("preview-1-x")]
    [InlineData("Docs")]
    [InlineData("a-name-that-is-longer-than-fifty-characters-for-sure-x")]
    public void AServerName_ThatIsTheOwnersOrTheStudios_IsRefused(string name) =>
        Assert.NotEmpty(PluginRules.CheckServer(Server() with { Name = name }, Options, Plugins));

    [Theory]
    [InlineData("Bearer ${GITHUB_TOKEN}", true)]
    [InlineData("${API_KEY}", true)]
    [InlineData("token ${A}${B}", true)]
    [InlineData("Bearer sk-live-0123456789abcdef", false)]
    [InlineData("Bearer ${TOKEN} and more", false)]
    [InlineData("${TOKEN:-fallback-secret}", false)]
    [InlineData("plain", false)]
    public void AHeaderTemplate_IsOnlyVariables_AfterOneWord(string template, bool valid) =>
        Assert.Equal(valid, PluginRules.IsTemplate(template));

    [Fact]
    public void AnInstalledHeader_HasATemplateOrAValue_NotBothNorNeither()
    {
        var both = Server(headers: [new PluginHeader("A", "${X}", "v")]);
        var neither = Server(headers: [new PluginHeader("A", null, null)]);
        var control = Server(headers: [new PluginHeader("A", null, "line\nbreak")]);

        Assert.NotEmpty(PluginRules.CheckHeaderValues(both));
        Assert.NotEmpty(PluginRules.CheckHeaderValues(neither));
        Assert.NotEmpty(PluginRules.CheckHeaderValues(control));
        Assert.Empty(PluginRules.CheckHeaderValues(Server(headers: [new PluginHeader("A", null, "value"), new PluginHeader("B", "${B}", null)])));
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("0.1")]
    public void APluginManifest_ReadsBack_AsTheSamePlugin(string? version)
    {
        var plugin = Plugin(version);

        var (read, problem) = PluginManifest.Read("docs", PluginManifest.Render(plugin));

        Assert.Null(problem);
        Assert.Equal(JsonSerializer.Serialize(plugin), JsonSerializer.Serialize(read));
        Assert.Empty(PluginRules.CheckPlugin(read!));
    }

    public static TheoryData<string, string> NotOurManifest => new()
    {
        { "a header value", "  template: Bearer ${DOCS_TOKEN}\n|  template: Bearer ${DOCS_TOKEN}\n  value: abc\n" },
        { "an extra key", "name: docs\n|name: docs\nhooks: curl evil\n" },
        { "another name", "name: docs\n|name: other\n" },
        { "grants", "agents:\n|grants:\n- company\nagents:\n" }
    };

    [Theory]
    [MemberData(nameof(NotOurManifest))]
    public void APluginManifest_OfAnyOtherShape_IsNotOurs(string what, string edit)
    {
        var yaml = PluginManifest.Render(Plugin());
        var (from, to) = (edit.Split('|')[0], edit.Split('|')[1]);
        Assert.Contains(from, yaml);

        Assert.True(PluginManifest.Read("docs", yaml.Replace(from, to)).Plugin is null, what);
    }

    [Fact]
    public void APluginManifest_IsJudged_SourceHeadersAndNotes()
    {
        var bad = Plugin() with
        {
            Source = new PluginSource("git", "https://github.com/acme/plugins", "main", "../../etc", "nothex"),
            Headers = [new PluginHeaderTemplate("unknown", "Authorization", "Bearer literal-token")],
            Notes = [new PluginNote("x", "fine", "y")]
        };

        var problems = string.Join(" ", PluginRules.CheckPlugin(bad));

        Assert.Contains("Source commit", problems);
        Assert.Contains("Source path", problems);
        Assert.Contains("Source digest", problems);
        Assert.Contains("which the plugin does not list", problems);
        Assert.Contains("a template is only variables", problems);
        Assert.Contains("Each note", problems);
    }

    [Fact]
    public void APluginsAgent_MayUseItsServer_WithNoToolNames_ButAnAllowListedOneStillNeedsThem()
    {
        var agent = new AgentDraft("doc-helper", "Helps with docs", "default-model-config", "Answer from the docs.", [],
            [new McpToolChoice("docs-search", [])], [], null);
        var (agentYaml, grantsYaml) = AgentManifest.Render(agent, "kagent");

        var (read, problem) = AgentManifest.Read("doc-helper", agentYaml, grantsYaml, "kagent");

        Assert.Null(problem);
        Assert.DoesNotContain("toolNames", agentYaml);
        Assert.Empty(DraftRules.Check(read!, Options, new Dictionary<string, string> { ["docs-search"] = "docs" }, ["docs-search"]));
        Assert.Contains(DraftRules.Check(read!, Options), p => p.Contains("not on the allow-list"));
        Assert.Contains(DraftRules.Check(read! with { McpTools = [new McpToolChoice("jira", [])] }, Options, new Dictionary<string, string> { ["docs-search"] = "docs" }),
            p => p.Contains("name at least one tool"));
        Assert.Contains("grants: []", grantsYaml);
    }

    [Fact]
    public void APluginAgent_IsARecipe_OnlyItsPluginsServers()
    {
        var agent = new AgentDraft("doc-helper", "d", "default-model-config", "x", [], [new McpToolChoice("jira", ["search"])],
            [new StudioGrant("company", true, false)], 7);

        var problems = string.Join(" ", PluginRules.CheckRecipe(agent, ["docs-search"]));

        Assert.Contains("only its plugin's servers", problems);
        Assert.Contains("never has memory grants", problems);
    }

    [Theory]
    [InlineData("ghp_0123456789abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("use sk-ant-api03-abcdefghijklmnopqrstuvwxyz to call")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc")]
    public void SomethingThatLooksLikeACredential_IsFound(string text) => Assert.NotNull(SecretScan.Find(text));

    [Theory]
    [InlineData("Bearer ${GITHUB_TOKEN}")]
    [InlineData("Ask for a bearer token if the user has none.")]
    [InlineData("sk-short")]
    public void OrdinaryText_IsNot(string text) => Assert.Null(SecretScan.Find(text));

    // D220: derived from nothing, so it confirms no guess.
    [Fact]
    public void TheFingerprint_IsRandom_TwelveHex()
    {
        var a = PluginHeaderFingerprint.New();

        Assert.Matches("^[a-f0-9]{12}$", a);
        Assert.NotEqual(a, PluginHeaderFingerprint.New());
        Assert.Matches("^[a-f0-9]{12}$", PluginHeaderFingerprint.None);
    }

    [Fact]
    public void TheSecretsCommand_NeverPrintsAValue()
    {
        var command = new SetPluginServerSecretsCommand(null!, "docs-search", new Dictionary<string, string> { ["Authorization"] = "Bearer super-secret-value" });

        Assert.DoesNotContain("super-secret-value", command.ToString());
        Assert.Contains("Authorization = ***", command.ToString());
    }
}
