using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface ICloudProviderService
{
    Task<List<CloudProvider>> GetProvidersAsync();
    Task<CloudProvider?> GetProviderAsync(string id);
    Task<bool> ConnectAsync(string id, object credentials);
    Task<bool> DisconnectAsync(string id);
    Task<Dictionary<string, int>> GetResourceCountsAsync(string providerId);
}
