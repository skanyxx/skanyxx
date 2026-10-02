using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface IHookService
{
    Task<List<Hook>> GetAllAsync();
    Task<Hook?> GetByIdAsync(string id);
    Task<Hook> CreateAsync(Hook hook);
    Task<bool> UpdateAsync(string id, Hook hook);
    Task<bool> EnableAsync(string id);
    Task<bool> DisableAsync(string id);
    Task<bool> DeleteAsync(string id);
    Task<bool> DeleteAsync(string ns, string name);
    Task<object> TestAsync(string id);
}
