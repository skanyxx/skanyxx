using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Memory.Services;

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
            if (!string.IsNullOrEmpty(category))
            {
                memories = memories.Where(m => m.Category == category).ToList();
            }
            return memories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get memories");
            return new List<MemoryItem>();
        }
    }

    public Task<MemoryItem?> GetByIdAsync(string id)
    {
        throw new NotImplementedException();
    }

    public async Task<MemoryItem> CreateAsync(CreateMemoryRequest request)
    {
        return await _kagent.CreateMemoryAsync(request);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        try
        {
            var parts = id.Split('/');
            var ns = parts.Length > 1 ? parts[0] : "kagent";
            var name = parts.Length > 1 ? parts[1] : id;
            await _kagent.DeleteMemoryAsync(ns, name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete memory {Id}", id);
            return false;
        }
    }

    public async Task<List<MemoryItem>> SearchAsync(string query)
    {
        return await _kagent.SearchMemoryAsync(query);
    }
}
