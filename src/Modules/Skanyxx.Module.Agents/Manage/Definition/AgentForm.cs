using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>
/// What a person may set on a kagent Declarative agent directly (D251), as kagent-ui's form: the rest of the spec
/// (deployment, env, BYO images, header secrets, prompt sources) is never taken from the request.
/// On an update <see cref="Namespace"/> and <see cref="Name"/> come from the route.
/// </summary>
internal sealed class AgentForm
{
    public string Namespace { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string SystemMessage { get; set; } = "";

    /// <summary>A ModelConfig name in <see cref="Namespace"/>.</summary>
    public string ModelConfig { get; set; } = "";

    /// <summary><c>go</c>, <c>python</c>, or empty for kagent's default.</summary>
    public string? Runtime { get; set; }

    public bool Stream { get; set; }

    /// <summary>RemoteMCPServers in <see cref="Namespace"/> and the tools the agent may call on each.</summary>
    public List<McpToolChoice> McpTools { get; set; } = [];

    /// <summary>Other agents in <see cref="Namespace"/> this agent may call as tools.</summary>
    public List<string> AgentTools { get; set; } = [];

    /// <summary>OCI skill images (<c>spec.skills.refs</c>).</summary>
    public List<string> Skills { get; set; } = [];

    /// <summary>The A2A card's skills (<c>declarative.a2aConfig.skills</c>).</summary>
    public List<A2aSkillForm> A2aSkills { get; set; } = [];
}
