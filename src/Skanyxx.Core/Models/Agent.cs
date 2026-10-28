namespace Skanyxx.Core.Models;

public class Agent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string Description { get; set; } = string.Empty;
    public string Namespace { get; set; } = "kagent";

    /// <summary>The CR's <c>metadata.generation</c>: changes with every spec edit, not with status (the answer cache keys on it).</summary>
    public string Revision { get; set; } = string.Empty;
    public bool Ready { get; set; }
    public bool Accepted { get; set; }
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Scaled to zero (<c>deployment.replicas: 0</c>): suspended on the Agents page, never offered in Chat (D257).</summary>
    public bool Suspended { get; set; }

    /// <summary>The MCP servers its spec names (<c>declarative.tools</c>), each with its tool names; none = every tool the server lists.</summary>
    public Dictionary<string, List<string>> McpToolNames { get; set; } = [];

    /// <summary>The ModelConfig a Declarative agent runs on (<c>declarative.modelConfig</c>); empty for BYO.</summary>
    public string ModelConfig { get; set; } = string.Empty;

    /// <summary>The agents its spec calls as tools (<c>declarative.tools[].agent.name</c>).</summary>
    public List<string> AgentToolNames { get; set; } = [];
    public int TasksToday { get; set; }
    public int AvgResponseMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
