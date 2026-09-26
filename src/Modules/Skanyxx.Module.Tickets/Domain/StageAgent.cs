using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>One kagent agent inside a stage. <see cref="Agent"/> is <c>namespace/name</c>.</summary>
public sealed record StageAgent(string Id, string Agent)
{
    [JsonIgnore]
    public string Namespace => Agent[..Agent.IndexOf('/')];
    [JsonIgnore]
    public string Name => Agent[(Agent.IndexOf('/') + 1)..];
}
