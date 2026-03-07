using System.Text.Json.Serialization;

namespace Skanyxx.Core.Models;

public class KAgentSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("agent_ref")]
    public string? AgentRef { get; set; }

    [JsonPropertyName("agent_id")]
    public string? AgentId { get; set; }

    [JsonPropertyName("updated_at")]
    public string? LastUpdateTime { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("deleted_at")]
    public string? DeletedAt { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
