namespace SkanyxxMaui.Models;

public class Memory
{
    public string Ref { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ApiKeySecretRef { get; set; } = string.Empty;
    public string ApiKeySecretKey { get; set; } = string.Empty;
    public Dictionary<string, object>? MemoryParams { get; set; }
}
