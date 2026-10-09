using System.Text.Json.Serialization;

namespace Skanyxx.Core.Models;

public class KAgentEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;
}

public class SessionWithEventsResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>kagent 0.10 answers <c>GET /api/sessions/{id}</c> with <c>{session, events}</c>.</summary>
    [JsonPropertyName("session")]
    public KAgentSession? Session { get; set; }

    [JsonPropertyName("events")]
    public List<KAgentEvent> Events { get; set; } = new();
}

public class ApiResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
