using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skanyxx.Core.Models;

/// <summary>kagent 0.10 answers <c>GET /api/sessions/{id}</c> with <c>{session, events}</c>.</summary>
public class KAgentSessionEnvelope
{
    [JsonPropertyName("session")]
    public KAgentSession? Session { get; set; }

    /// <summary>The session's events as kagent wrote them (ADK or Go runtime shapes); the Chat page reads them.</summary>
    [JsonPropertyName("events")]
    public List<JsonElement> Events { get; set; } = [];
}
