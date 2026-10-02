using System.Text.Json.Serialization;

namespace SkanyxxWeb.Models;

/// <summary>
/// Generic wrapper for KAgent API responses
/// KAgent returns responses in format: {"error": false, "data": [...], "message": "..."}
/// </summary>
public class KAgentResponse<T>
{
    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
