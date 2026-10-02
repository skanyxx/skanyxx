using SkanyxxWeb.Models;

namespace SkanyxxWeb.Interfaces;

public interface IMemoryService
{
    Task<List<MemoryItem>> GetAllAsync(string? category = null);
    Task<MemoryItem?> GetByIdAsync(string id);
    Task<MemoryItem> CreateAsync(CreateMemoryRequest request);
    Task<bool> DeleteAsync(string id);
    Task<List<MemoryItem>> SearchAsync(string query);
}
