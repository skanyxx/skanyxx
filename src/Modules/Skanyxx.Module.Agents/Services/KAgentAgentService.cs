using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Agents.Services;

public class KAgentAgentService(KAgentApiClient kagent) : IAgentService
{
    public Task<List<Agent>> GetAllAsync() => kagent.GetAgentsAsync();

    public Task<Agent?> GetAsync(string ns, string name, CancellationToken ct) => kagent.GetAgentAsync(ns, name, ct);
}
