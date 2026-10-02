using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface IAgentService
{
    Task<List<Agent>> GetAllAsync();
    Task<Agent?> GetByIdAsync(string id);
    Task<Agent> CreateAsync(CreateAgentRequest request);
    Task<bool> UpdateStatusAsync(string id, string status);
    Task<bool> DeleteAsync(string id);
}
