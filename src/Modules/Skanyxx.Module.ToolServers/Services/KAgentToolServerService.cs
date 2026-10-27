using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.ToolServers.Services;

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
        try
        {
            return await _kagent.GetToolServersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tool servers");
            return new List<ToolServer>();
        }
    }
}
