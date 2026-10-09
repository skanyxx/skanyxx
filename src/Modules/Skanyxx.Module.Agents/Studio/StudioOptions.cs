namespace Skanyxx.Module.Agents.Studio;

/// <summary>
/// <c>Studio</c> configuration. The studio is on when git is configured (<see cref="GitOptions.BaseUrl"/> and
/// <see cref="GitOptions.Token"/>); without it the pages and API answer 409 naming the settings.
/// </summary>
public sealed class StudioOptions
{
    public const string Section = "Studio";

    public GitOptions Git { get; set; } = new();

    /// <summary>The kagent namespace studio agents live in (and their ModelConfigs and MCP servers).</summary>
    public string Namespace { get; set; } = "kagent";

    /// <summary>
    /// <c>/mcp/memory</c> as kagent's pods reach it (e.g. <c>http://skanyxx.skanyxx.svc:8080/mcp/memory</c>); every
    /// studio agent with a memory grant gets its own RemoteMCPServer pointing here. Required once an agent has a grant.
    /// </summary>
    public string MemoryMcpUrl { get; set; } = "";

    /// <summary>RemoteMCPServers (names in <see cref="Namespace"/>) a builder may attach besides memory (D029).</summary>
    public List<string> McpServers { get; set; } = [];

    /// <summary>How often the reconciler compares main with kagent when nothing triggered it (seconds).</summary>
    public int ReconcileSeconds { get; set; } = 60;

    /// <summary>The ModelConfig the factory agent runs on (D5); the owner's model by default.</summary>
    public string FactoryModelConfig { get; set; } = "default-model-config";

    /// <summary>
    /// OCI registry prefixes skills may come from (D122), e.g. <c>ghcr.io/acme/skills/</c>. Empty (the default) turns
    /// skills off: a skill runs code in the agent's pod, so the owner chooses where it may come from.
    /// </summary>
    public List<string> SkillRegistries { get; set; } = [];

    /// <summary>
    /// The removal brake (D119): a pass removes at most this many merged agents from kagent (and never more than half of
    /// them, and none when main has no <c>agents/</c>) unless the owner confirms (<c>POST api/studio/confirm</c>).
    /// </summary>
    public int MaxRemovalsPerPass { get; set; } = 2;

    /// <summary>Open proposals one builder may have at a time (each can run a preview pod).</summary>
    public int MaxOpenProposalsPerBuilder { get; set; } = 10;

    /// <summary>How long a merge, preview or factory call waits for a running reconcile pass (D120) before giving up.</summary>
    public int LockWaitSeconds { get; set; } = 20;

    public bool Configured => !string.IsNullOrWhiteSpace(Git.BaseUrl) && !string.IsNullOrWhiteSpace(Git.Token);

    public sealed class GitOptions
    {
        /// <summary>The bundled Gitea (D023), e.g. <c>http://gitea.skanyxx.svc:3000</c>; locally <c>http://localhost:3300</c>.</summary>
        public string BaseUrl { get; set; } = "";

        /// <summary>The token of the one git account Skanyxx uses. Never logged, never returned.</summary>
        public string Token { get; set; } = "";

        /// <summary>That account's user name: the only one branch protection lets merge. Not the org's name (Gitea users and orgs share one namespace).</summary>
        public string User { get; set; } = "skanyxx-bot";

        public string Owner { get; set; } = "skanyxx";

        public string Repo { get; set; } = "skanyxx-agents";

        public string Branch { get; set; } = "main";

        public int TimeoutSeconds { get; set; } = 15;
    }
}
