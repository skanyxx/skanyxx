using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Hooks.Services;

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
        try
        {
            return await _kagent.GetHooksAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get hooks");
            return new List<Hook>();
        }
    }

    public Task<Hook?> GetByIdAsync(string id)
    {
        throw new NotImplementedException();
    }

    public async Task<Hook> CreateAsync(Hook hook)
    {
        return await _kagent.CreateHookAsync(hook);
    }

    public Task<bool> UpdateAsync(string id, Hook hook)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> EnableAsync(string id)
    {
        return await _kagent.EnableHookAsync(id);
    }

    public async Task<bool> DisableAsync(string id)
    {
        return await _kagent.DisableHookAsync(id);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        // Parse namespace/name from id
        var parts = id.Split('/');
        if (parts.Length == 2)
        {
            return await DeleteAsync(parts[0], parts[1]);
        }
        return await DeleteAsync("kagent", id);
    }

    public async Task<bool> DeleteAsync(string ns, string name)
    {
        try
        {
            await _kagent.DeleteHookAsync(ns, name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete hook {Namespace}/{Name}", ns, name);
            return false;
        }
    }

    public Task<object> TestAsync(string id)
    {
        throw new NotImplementedException();
    }
}
