namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>
/// The Add/Edit form: a RemoteMCPServer. On an edit, <see cref="Namespace"/> and <see cref="Name"/> come from the route.
/// </summary>
public sealed class ToolServerForm
{
    public string? Namespace { get; set; }

    public string? Name { get; set; }

    public string? Url { get; set; }

    /// <summary><see cref="ToolServerProtocols.StreamableHttp"/> (default) or <see cref="ToolServerProtocols.Sse"/>.</summary>
    public string? Protocol { get; set; }

    public int? TimeoutSeconds { get; set; }

    public string? Description { get; set; }

    public List<HeaderInput> Headers { get; set; } = [];
}
