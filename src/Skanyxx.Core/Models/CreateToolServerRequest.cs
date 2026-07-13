namespace Skanyxx.Core.Models;

public class CreateToolServerRequest
{
    public string Type { get; set; } = "RemoteMCPServer";
    public RemoteMCPServer? RemoteMCPServer { get; set; }
    public MCPServer? McpServer { get; set; }
}

public class RemoteMCPServer
{
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}

public class MCPServer
{
    public string Name { get; set; } = string.Empty;
    public List<string> Command { get; set; } = new();
}
