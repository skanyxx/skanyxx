using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class KAgentMemoryService : IMemoryService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentMemoryService> _logger;

    public KAgentMemoryService(KAgentApiClient kagent, ILogger<KAgentMemoryService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<List<MemoryItem>> GetAllAsync(string? category = null)
    {
        try
        {
            var memories = await _kagent.GetMemoriesAsync();
            return string.IsNullOrEmpty(category) ? memories : memories.Where(m => m.Category == category).ToList();
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get memories"); return new(); }
    }

    public Task<MemoryItem?> GetByIdAsync(string id) => throw new NotImplementedException();

    public async Task<MemoryItem> CreateAsync(CreateMemoryRequest request) =>
        await _kagent.CreateMemoryAsync(request);

    public async Task<bool> DeleteAsync(string id)
    {
        try
        {
            var parts = id.Split('/');
            await _kagent.DeleteMemoryAsync(parts.Length > 1 ? parts[0] : "kagent", parts.Length > 1 ? parts[1] : id);
            return true;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to delete memory {Id}", id); return false; }
    }

    public async Task<List<MemoryItem>> SearchAsync(string query) =>
        await _kagent.SearchMemoryAsync(query);
}
