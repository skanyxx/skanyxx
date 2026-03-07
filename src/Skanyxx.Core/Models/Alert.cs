using System.Text.Json.Serialization;

namespace Skanyxx.Core.Models;

public class Alert
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("hookName")]
    public string HookName { get; set; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("resourceName")]
    public string ResourceName { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "firing";

    [JsonPropertyName("firstSeen")]
    public string FirstSeen { get; set; } = string.Empty;

    [JsonPropertyName("lastSeen")]
    public string LastSeen { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("remediationStatus")]
    public string? RemediationStatus { get; set; }
}
