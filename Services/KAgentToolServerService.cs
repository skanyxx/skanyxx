using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class KAgentToolServerService : IToolServerService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentToolServerService> _logger;

    public KAgentToolServerService(KAgentApiClient kagent, ILogger<KAgentToolServerService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<List<ToolServer>> GetAllAsync()
    {
        try { return await _kagent.GetToolServersAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get tool servers"); return new(); }
    }

    public Task<ToolServer?> GetByIdAsync(string id) => throw new NotImplementedException();
    public Task<bool> PingAsync(string id)            => throw new NotImplementedException();
    public Task<bool> RestartAsync(string id)         => throw new NotImplementedException();
    public Task<List<ToolCall>> GetRecentCallsAsync(int limit = 10) => Task.FromResult(new List<ToolCall>());

    public async Task<object> InvokeToolAsync(string serverId, string toolName, object parameters) =>
        await _kagent.InvokeToolAsync(serverId, toolName, parameters);
}
