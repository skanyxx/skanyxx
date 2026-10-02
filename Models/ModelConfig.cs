using System.Text.Json.Serialization;

namespace SkanyxxWeb.Models;

public class ModelConfig
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("providerName")]
    public string ProviderName { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("apiKeySecretRef")]
    public string ApiKeySecretRef { get; set; } = string.Empty;

    [JsonPropertyName("apiKeySecretKey")]
    public string ApiKeySecretKey { get; set; } = string.Empty;

    [JsonPropertyName("modelParams")]
    public Dictionary<string, object>? ModelParams { get; set; }
}
