using SkanyxxWeb.Models;

namespace SkanyxxWeb.Interfaces;

public interface IHookService
{
    Task<List<Hook>> GetAllAsync();
    Task<Hook?> GetByIdAsync(string id);
    Task<Hook> CreateAsync(Hook hook);
    Task<bool> UpdateAsync(string id, Hook hook);
    Task<bool> EnableAsync(string id);
    Task<bool> DisableAsync(string id);
    Task<bool> DeleteAsync(string id);
    Task<object> TestAsync(string id);
    Task<List<HookExecution>> GetExecutionsAsync(int limit = 10);
}
