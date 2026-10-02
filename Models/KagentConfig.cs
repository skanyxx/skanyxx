namespace SkanyxxWeb.Models;

public class KAgentConfig
{
    public string BaseUrl { get; set; } = "localhost";
    public int Port { get; set; } = 8083;
    public string Protocol { get; set; } = "http";
    public string? Token { get; set; }
    public int Timeout { get; set; } = 30000;
    public string? IngressUrl { get; set; }

    public string GetFullUrl()
    {
        if (!string.IsNullOrEmpty(IngressUrl))
            return IngressUrl;
        return $"{Protocol}://{BaseUrl}:{Port}";
    }
}
