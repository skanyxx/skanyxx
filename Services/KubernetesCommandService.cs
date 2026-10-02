using SkanyxxWeb.Interfaces;
using System.Text.Json;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class KubernetesCommandService : IKubernetesService
{
    private readonly ICommandExecutionService _commandService;
    private readonly ILogger<KubernetesCommandService> _logger;

    public KubernetesCommandService(ICommandExecutionService commandService, ILogger<KubernetesCommandService> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    public async Task<List<Agent>> GetKubernetesAgentsAsync()
    {
        var result = await _commandService.ExecuteKubectlAsync(new[] { "get", "deployments", "-A", "-l", "app.kubernetes.io/component=agent", "-o", "json" });
        if (!result.Success) { _logger.LogWarning("kubectl failed: {Error}", result.Stderr); return new(); }
        try
        {
            var json   = JsonDocument.Parse(result.Stdout);
            var agents = new List<Agent>();
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var metadata = item.GetProperty("metadata");
                var status   = item.GetProperty("status");
                agents.Add(new Agent
                {
                    Id   = metadata.GetProperty("uid").GetString() ?? "",
                    Name = metadata.GetProperty("name").GetString() ?? "",
                    Type = metadata.TryGetProperty("labels", out var labels) && labels.TryGetProperty("app.kubernetes.io/component", out var comp) ? comp.GetString() ?? "Unknown" : "Unknown",
                    Status = status.TryGetProperty("availableReplicas", out var replicas) && replicas.GetInt32() > 0 ? "Active" : "Inactive"
                });
            }
            return agents;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to parse kubectl output"); return new(); }
    }

    public async Task<List<SystemStatus>> GetClusterStatusAsync()
    {
        var statuses = new List<SystemStatus>();
        var apiResult = await _commandService.ExecuteKubectlAsync(new[] { "cluster-info" });
        statuses.Add(new SystemStatus { ServiceName = "Kubernetes API", Status = apiResult.Success ? "Online" : "Offline", LatencyMs = 0 });

        var nodesResult = await _commandService.ExecuteKubectlAsync(new[] { "get", "nodes", "-o", "json" });
        if (nodesResult.Success)
        {
            try
            {
                var json = JsonDocument.Parse(nodesResult.Stdout);
                foreach (var node in json.RootElement.GetProperty("items").EnumerateArray())
                {
                    var name  = node.GetProperty("metadata").GetProperty("name").GetString();
                    var ready = node.GetProperty("status").GetProperty("conditions").EnumerateArray()
                        .Any(c => c.GetProperty("type").GetString() == "Ready" && c.GetProperty("status").GetString() == "True");
                    statuses.Add(new SystemStatus { ServiceName = $"Node: {name}", Status = ready ? "Online" : "NotReady", LatencyMs = 0 });
                }
            }
            catch { }
        }
        return statuses;
    }

    public async Task<object> GetPodsAsync(string? ns = null)
    {
        var args = ns is null ? new[] { "get", "pods", "-o", "json", "-A" } : new[] { "get", "pods", "-o", "json", "-n", ns };
        var result = await _commandService.ExecuteKubectlAsync(args);
        if (!result.Success) throw new Exception($"kubectl failed: {result.Stderr}");
        return JsonDocument.Parse(result.Stdout).RootElement;
    }

    public async Task<object> GetDeploymentsAsync(string? ns = null)
    {
        var args = ns is null ? new[] { "get", "deployments", "-o", "json", "-A" } : new[] { "get", "deployments", "-o", "json", "-n", ns };
        var result = await _commandService.ExecuteKubectlAsync(args);
        if (!result.Success) throw new Exception($"kubectl failed: {result.Stderr}");
        return JsonDocument.Parse(result.Stdout).RootElement;
    }

    public async Task<object> GetServicesAsync(string? ns = null)
    {
        var args = ns is null ? new[] { "get", "services", "-o", "json", "-A" } : new[] { "get", "services", "-o", "json", "-n", ns };
        var result = await _commandService.ExecuteKubectlAsync(args);
        if (!result.Success) throw new Exception($"kubectl failed: {result.Stderr}");
        return JsonDocument.Parse(result.Stdout).RootElement;
    }

    public async Task<bool> ScaleDeploymentAsync(string name, string ns, int replicas) =>
        (await _commandService.ExecuteKubectlAsync(new[] { "scale", "deployment", name, "-n", ns, $"--replicas={replicas}" })).Success;

    public async Task<bool> RestartDeploymentAsync(string name, string ns) =>
        (await _commandService.ExecuteKubectlAsync(new[] { "rollout", "restart", "deployment", name, "-n", ns })).Success;
}
