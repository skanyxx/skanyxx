using SkanyxxWeb.Models;

namespace SkanyxxWeb.Interfaces;

public interface IToolServerService
{
    Task<List<ToolServer>> GetAllAsync();
    Task<ToolServer?> GetByIdAsync(string id);
    Task<bool> PingAsync(string id);
    Task<bool> RestartAsync(string id);
    Task<List<ToolCall>> GetRecentCallsAsync(int limit = 10);
    Task<object> InvokeToolAsync(string serverId, string toolName, object parameters);
}
