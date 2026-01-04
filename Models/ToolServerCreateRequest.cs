namespace SkanyxxMaui.Models;

public class ToolServerCreateRequest
{
    public ToolServerType Type { get; set; }
    public RemoteMCPServer? RemoteMCPServer { get; set; }
    public MCPServer? MCPServer { get; set; }
}
