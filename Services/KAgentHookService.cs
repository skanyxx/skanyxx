using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class KAgentHookService : IHookService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentHookService> _logger;

    public KAgentHookService(KAgentApiClient kagent, ILogger<KAgentHookService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<List<Hook>> GetAllAsync()
    {
        try { return await _kagent.GetHooksAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get hooks"); return new(); }
    }

    public Task<Hook?> GetByIdAsync(string id)          => throw new NotImplementedException();
    public Task<bool> UpdateAsync(string id, Hook hook) => throw new NotImplementedException();
    public Task<object> TestAsync(string id)            => throw new NotImplementedException();

    public async Task<Hook> CreateAsync(Hook hook) => await _kagent.CreateHookAsync(hook);
    public async Task<bool> EnableAsync(string id)  => await _kagent.EnableHookAsync(id);
    public async Task<bool> DisableAsync(string id) => await _kagent.DisableHookAsync(id);

    public async Task<bool> DeleteAsync(string id)
    {
        var parts = id.Split('/');
        return parts.Length == 2
            ? await DeleteAsync(parts[0], parts[1])
            : await DeleteAsync("kagent", id);
    }

    public async Task<bool> DeleteAsync(string ns, string name)
    {
        try { await _kagent.DeleteHookAsync(ns, name); return true; }
        catch (Exception ex) { _logger.LogError(ex, "Failed to delete hook {Namespace}/{Name}", ns, name); return false; }
    }
}
