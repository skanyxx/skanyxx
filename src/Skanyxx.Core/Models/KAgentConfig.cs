namespace Skanyxx.Core.Models;

public class KAgentConfig
{
    public string BaseUrl { get; set; } = "localhost";
    public int Port { get; set; } = 8083;
    public string Protocol { get; set; } = "http";
    public string? Token { get; set; }

    /// <summary>Bound on every control call (agents, sessions, ModelConfig) — the owner's landing page waits on one.</summary>
    public int ControlTimeoutSeconds { get; set; } = 10;

    /// <summary>Bound on one blocking chat turn (A2A <c>message/send</c>): a local model with tool calls can take minutes.</summary>
    public int ChatTimeoutSeconds { get; set; } = 300;
    public string? IngressUrl { get; set; }

    /// <summary>The ModelConfig every seed agent uses (<c>namespace/name</c>); the owner sets it in the model step (D3).</summary>
    public string ModelConfig { get; set; } = "kagent/default-model-config";

    public string GetFullUrl()
    {
        if (!string.IsNullOrEmpty(IngressUrl))
            return IngressUrl;
        return $"{Protocol}://{BaseUrl}:{Port}";
    }
}
