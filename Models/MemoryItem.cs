using System.Text.Json.Serialization;

namespace SkanyxxWeb.Models;

// KAgent Memory Provider model (for vector databases like Pinecone)
public class MemoryItem
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("providerName")]
    public string ProviderName { get; set; } = string.Empty;

    [JsonPropertyName("apiKeySecretRef")]
    public string ApiKeySecretRef { get; set; } = string.Empty;

    [JsonPropertyName("apiKeySecretKey")]
    public string ApiKeySecretKey { get; set; } = string.Empty;

    [JsonPropertyName("memoryParams")]
    public Dictionary<string, object> MemoryParams { get; set; } = new();

    // Legacy fields for backward compatibility
    public string Id { get => Ref; set => Ref = value; }
    public string Category { get; set; } = string.Empty;
    public string Title { get => Ref; set => Ref = value; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateMemoryRequest
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public MemoryProvider? Provider { get; set; }

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("pineconeParams")]
    public PineconeParams? PineconeParams { get; set; }

    // Legacy fields
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? Category { get; set; }
}

public class MemoryProvider
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "pinecone";
}

public class PineconeParams
{
    [JsonPropertyName("index")]
    public string Index { get; set; } = string.Empty;

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }
}
