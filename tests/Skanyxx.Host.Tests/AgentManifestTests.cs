using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The studio's files (D1): what the form renders reads back to the same form, and anything else — another kind,
/// another key, a value YAML would type differently — is not a studio agent and never merges.
/// </summary>
public sealed class AgentManifestTests
{
    private const string Digest = "@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly StudioOptions Options = new() { McpServers = ["jira"], SkillRegistries = ["ghcr.io/acme/skills/", "registry.local:5000/team"] };

    private static AgentDraft Draft(string description = "Answers refunds", string instructions = "Answer.\nBe brief.") => new(
        "refund-helper", description, "default-model-config", instructions,
        ["ghcr.io/acme/skills/refunds:1.2" + Digest],
        [new McpToolChoice("jira", ["search_issues", "get_issue"])],
        [new StudioGrant("company", true, false), new StudioGrant("team:billing", true, true)],
        14);

    [Theory]
    [InlineData("true")]
    [InlineData("123")]
    [InlineData("null")]
    [InlineData("- not a list")]
    [InlineData("key: value")]
    [InlineData("# not a comment")]
    [InlineData("\"quoted\" and 'single'")]
    [InlineData("Ünïcødé ✓")]
    [InlineData("")]
    public void WhatTheFormRenders_ReadsBack_AsTheSameForm(string description)
    {
        var draft = Draft(description, $"  Indented first line\n\tTabbed {description}\nTrailing space \nlast");
        var (agent, grants) = AgentManifest.Render(draft, "kagent");

        var (read, problem) = AgentManifest.Read("refund-helper", agent, grants, "kagent");

        Assert.Null(problem);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(draft), System.Text.Json.JsonSerializer.Serialize(read));
        Assert.Empty(DraftRules.Check(read!, Options));
    }

    public static TheoryData<string, string> NotOurs => new()
    {
        { "another kind", "kind: Agent\n|kind: ConfigMap\n" },
        { "a BYO image", "type: Declarative\n|type: BYO\n  byo:\n    deployment:\n      image: evil:1\n" },
        { "an extra spec field", "  description: Answers refunds\n|  description: Answers refunds\n  allowedNamespaces:\n    from: All\n" },
        { "a deployment", "    modelConfig: default-model-config\n    systemMessage|    deployment:\n      serviceAccountName: cluster-admin\n    modelConfig: default-model-config\n    systemMessage" },
        { "labels", "  namespace: kagent\n|  namespace: kagent\n  labels:\n    skanyxx.dev/merged: \"true\"\n" },
        { "another namespace", "  namespace: kagent\n|  namespace: kube-system\n" },
        { "another memory server", "name: skanyxx-memory-refund-helper|name: skanyxx-memory-seed" },
        { "upsert without the grant", "    - memory_search\n|    - memory_search\n    - memory_upsert\n" },
        { "a typed scalar", "ttlDays: 14|ttlDays: fourteen" },
        { "a YAML tag", "kind: Agent|kind: !!python/object:os.system Agent" },
        { "a duplicate key", "kind: Agent\n|kind: Agent\nkind: Agent\n" },
        { "an agent as a tool", "    - type: McpServer\n      mcpServer:\n        name: jira|    - type: Agent\n      agent:\n        name: seed\n    - type: McpServer\n      mcpServer:\n        name: jira" },
        { "not YAML", "apiVersion|{{{ [" }
    };

    [Theory]
    [MemberData(nameof(NotOurs))]
    public void AnAgentFile_ThatIsNotExactlyAStudioAgent_IsRefused(string what, string edit)
    {
        var (agent, grants) = AgentManifest.Render(Draft(), "kagent");
        var (from, to) = (edit.Split('|')[0], edit.Split('|')[1]);
        Assert.True(agent.Contains(from), $"{what}: the edit must apply");

        var (read, problem) = AgentManifest.Read("refund-helper", agent.Replace(from, to), grants, "kagent");

        Assert.Null(read);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("search: true\n|search: yes\n")]
    [InlineData("agent: refund-helper|agent: seed")]
    [InlineData("grants:\n|owner: me\ngrants:\n")]
    public void AGrantsFile_ThatIsNotExactlyTheAgentsGrants_IsRefused(string edit)
    {
        var (agent, grants) = AgentManifest.Render(Draft(), "kagent");
        var (from, to) = (edit.Split('|')[0], edit.Split('|')[1]);
        Assert.Contains(from, grants);

        Assert.Null(AgentManifest.Read("refund-helper", agent, grants.Replace(from, to), "kagent").Draft);
    }

    [Fact]
    public void FilesInAnotherAgentsFolder_AreRefused()
    {
        var (agent, grants) = AgentManifest.Render(Draft(), "kagent");

        Assert.Contains("must name the agent", AgentManifest.Read("other", agent, grants, "kagent").Problem);
    }

    /// <summary>D122 (M7): a skill is code in the agent's pod — from an allow-listed registry, pinned by digest.</summary>
    [Theory]
    [InlineData("ghcr.io/acme/skills/refunds" + Digest, true)]
    [InlineData("ghcr.io/acme/skills/refunds:1.2" + Digest, true)]
    [InlineData("registry.local:5000/team/skill" + Digest, true)]
    [InlineData("ghcr.io/acme/skills/refunds:1.2", false)]               // a tag can move after review
    [InlineData("ghcr.io/evil/skills/refunds" + Digest, false)]          // not an allow-listed registry
    [InlineData("ghcr.io/acme/skills-evil/refunds" + Digest, false)]     // a prefix is a path, not a string
    [InlineData("registry.local:5000/teamx/skill" + Digest, false)]
    [InlineData("https://evil.example/skill", false)]
    [InlineData("skill with space", false)]
    public void Skills_AreDigestPinned_FromAllowListedRegistries(string skill, bool valid) =>
        Assert.Equal(valid, DraftRules.Check(Draft() with { Skills = [skill] }, Options).Count == 0);

    [Fact]
    public void Skills_AreOff_UntilTheOwnerAllowListsARegistry()
    {
        var problems = DraftRules.Check(Draft(), new StudioOptions { McpServers = ["jira"] });

        Assert.Contains(problems, p => p.Contains("Skills are off"));
        Assert.Empty(DraftRules.Check(Draft() with { Skills = [] }, new StudioOptions { McpServers = ["jira"] }));
    }

    [Fact]
    public void APreview_RunsWithoutSkills() =>
        Assert.Empty(Skanyxx.Module.Agents.Studio.Reconcile.KAgentObjects.Preview(Draft(), 3).Skills);

    /// <summary>m10: kagent makes a Service from the name (DNS-1035: a letter first), and the preview's memory server name must fit 63.</summary>
    [Theory]
    [InlineData("1refunds", false)]
    [InlineData("refunds-1", true)]
    [InlineData("a23456789012345678901234567890123", true)]   // 33
    [InlineData("a234567890123456789012345678901234", false)] // 34
    public void Names_StartWithALetter_AndFitEveryDerivedName(string name, bool valid)
    {
        Assert.Equal(valid, StudioNames.IsValidName(name));
        if (valid)
            Assert.True(StudioNames.IsKubeName(StudioNames.MemoryServer(StudioNames.PreviewAgent(999_999, name))));
    }

    // --- M1: untrusted YAML never costs the process ---

    [Fact]
    public void AnAliasBomb_IsRefused_BeforeItExpands()
    {
        var bomb = "a: &a [x, x, x, x, x, x, x, x, x]\nb: &b [*a, *a, *a, *a, *a, *a, *a, *a, *a]\nc: &c [*b, *b, *b, *b, *b, *b, *b, *b, *b]\n"
            + "d: &d [*c, *c, *c, *c, *c, *c, *c, *c, *c]\ne: &e [*d, *d, *d, *d, *d, *d, *d, *d, *d]\nf: &f [*e, *e, *e, *e, *e, *e, *e, *e, *e]\n"
            + "g: &g [*f, *f, *f, *f, *f, *f, *f, *f, *f]\nh: [*g, *g, *g, *g, *g, *g, *g, *g, *g]\n";
        var (agent, grants) = AgentManifest.Render(Draft(), "kagent");
        var before = GC.GetTotalAllocatedBytes();

        var (read, problem) = AgentManifest.Read("refund-helper", bomb, grants, "kagent");
        var anchorOnly = AgentManifest.Read("refund-helper", agent.Replace("kind: Agent", "kind: &k Agent"), grants, "kagent");
        var aliasOnly = AgentManifest.Read("refund-helper", agent.Replace("kind: Agent", "kind: *k"), grants, "kagent");

        Assert.Null(read);
        Assert.Contains("anchors are not allowed", problem);
        Assert.Contains("anchors are not allowed", anchorOnly.Problem);
        Assert.Contains("aliases are not allowed", aliasOnly.Problem);
        Assert.True(GC.GetTotalAllocatedBytes() - before < 50_000_000, "the bomb was expanded");
    }

    /// <summary>
    /// 10,000 levels inside the size cap overflowed the stack (exit 134, uncatchable). Run on a 256 KB stack: if anything
    /// still recursed per level, this test would kill the test host instead of failing.
    /// </summary>
    [Theory]
    [InlineData("[", "]")]
    [InlineData("{a: ", "}")]
    [InlineData("- ", "")]
    public void DeepNesting_IsRefused_WithoutRecursingPerLevel(string open, string close)
    {
        var deep = close.Length > 0
            ? string.Concat(Enumerable.Repeat(open, 10_000)) + string.Concat(Enumerable.Repeat(close, 10_000))
            : string.Concat(Enumerable.Range(0, 500).Select(i => new string(' ', i * 2) + open + "\n"));
        string? problem = null;
        var thread = new Thread(() => problem = AgentManifest.Read("refund-helper", deep, "agent: refund-helper\ngrants: []\n", "kagent").Problem, 256 * 1024);

        thread.Start();
        thread.Join();

        Assert.NotNull(problem);
        Assert.Contains("Not valid YAML", problem);
    }

    [Fact]
    public void TooManyNodes_OrASecondDocument_AreRefused()
    {
        var (agent, grants) = AgentManifest.Render(Draft(), "kagent");
        var wide = "items:\n" + string.Concat(Enumerable.Repeat("- x\n", YamlTree.MaxEvents));

        Assert.Contains("More than", AgentManifest.Read("refund-helper", wide, grants, "kagent").Problem);
        Assert.Contains("One YAML document", AgentManifest.Read("refund-helper", agent + "---\nkind: Secret\n", grants, "kagent").Problem);
    }

    /// <summary>I4: bidi overrides and zero-width characters would show a supervisor other text than the agent gets.</summary>
    [Theory]
    [InlineData("name", "refund\u200Bhelper")]
    [InlineData("description", "Answers refund \u202Eseicilop questions")]
    [InlineData("description", "Answers\u2066 refunds")]
    [InlineData("instructions", "Answer refunds.\nNever \u200Dpromise more.")]
    [InlineData("instructions", "Answer refunds.\uFEFF")]
    public void FormatCharacters_AreRefused(string field, string text)
    {
        var draft = field switch
        {
            "name" => Draft() with { Name = text },
            "description" => Draft() with { Description = text },
            _ => Draft() with { Instructions = text }
        };

        Assert.Contains(DraftRules.Check(draft, Options), p => p.StartsWith(field, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(DraftRules.Check(Draft() with { Description = "Ünïcødé ✓ refunds", Instructions = "Line one.\n\tLine two." }, Options));
    }

    [Fact]
    public void TheFormRules_RefuseWhatAStudioAgentMayNotHave()
    {
        var problems = DraftRules.Check(Draft() with
        {
            Name = "seed",
            McpTools = [new McpToolChoice("skanyxx-memory-seed", ["memory_upsert"]), new McpToolChoice("kubectl", ["apply"])],
            Grants = [new StudioGrant("personal", true, true), new StudioGrant("company", false, false)],
            MemoryTtlDays = 0
        }, Options);

        Assert.Contains(problems, p => p.Contains("reserved"));
        Assert.Contains(problems, p => p.Contains("'skanyxx-memory-seed' is not on the allow-list"));
        Assert.Contains(problems, p => p.Contains("'kubectl' is not on the allow-list"));
        Assert.Contains(problems, p => p.Contains("no personal scope"));
        Assert.Contains(problems, p => p.Contains("give search, upsert or both"));
        Assert.Contains(problems, p => p.Contains("TTL"));
    }
}
