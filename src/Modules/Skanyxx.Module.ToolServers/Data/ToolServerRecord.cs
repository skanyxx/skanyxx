namespace Skanyxx.Module.ToolServers.Data;

/// <summary>
/// What Skanyxx sent kagent for a RemoteMCPServer it created: kagent 0.10.2 lists a server's name, kind and tools only, so
/// the page reads the URL, protocol and header sources from here. Never a header value.
/// </summary>
public sealed class ToolServerRecord
{
    public string Namespace { get; set; } = "";

    public string Name { get; set; } = "";

    public string Url { get; set; } = "";

    public string Protocol { get; set; } = "";

    public int? TimeoutSeconds { get; set; }

    public string Description { get; set; } = "";

    public List<ToolServerHeader> Headers { get; set; } = [];

    public string? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string Ref => $"{Namespace}/{Name}";
}
