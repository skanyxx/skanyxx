using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Agents.Services;

public class KAgentAgentService : IAgentService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentAgentService> _logger;

    public KAgentAgentService(KAgentApiClient kagent, ILogger<KAgentAgentService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public Task<List<Agent>> GetAllAsync() => _kagent.GetAgentsAsync();

    public async Task<Agent?> GetByIdAsync(string id)
    {
        try { return await _kagent.GetAgentAsync(id); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get agent {Id}", id);
            return null;
        }
    }
}
