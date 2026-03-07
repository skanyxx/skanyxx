using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface IKubernetesService
{
    Task<List<Agent>> GetKubernetesAgentsAsync();
    Task<List<SystemStatus>> GetClusterStatusAsync();
    Task<object> GetPodsAsync(string? ns = null);
    Task<object> GetDeploymentsAsync(string? ns = null);
    Task<object> GetServicesAsync(string? ns = null);
    Task<bool> ScaleDeploymentAsync(string name, string ns, int replicas);
    Task<bool> RestartDeploymentAsync(string name, string ns);
}
