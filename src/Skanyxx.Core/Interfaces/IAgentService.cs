using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface IAgentService
{
    Task<List<Agent>> GetAllAsync();
    Task<Agent?> GetAsync(string ns, string name, CancellationToken ct);
}
