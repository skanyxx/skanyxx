using SkanyxxWeb.Interfaces;
using System.Text.Json;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class KAgentAgentService : IAgentService
{
    private readonly KAgentApiClient _kagent;
    private readonly ICommandExecutionService _commandService;
    private readonly ILogger<KAgentAgentService> _logger;

    public KAgentAgentService(KAgentApiClient kagent, ICommandExecutionService commandService, ILogger<KAgentAgentService> logger)
    {
        _kagent = kagent;
        _commandService = commandService;
        _logger = logger;
    }

    public async Task<List<Agent>> GetAllAsync()
    {
        try
        {
            var agents = await _kagent.GetAgentsAsync();
            if (agents.Count > 0) return agents;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KAgent API failed, falling back to Kubernetes CRD");
        }
        return await GetAgentsFromKubernetesAsync();
    }

    private async Task<List<Agent>> GetAgentsFromKubernetesAsync()
    {
        try
        {
            var result = await _commandService.ExecuteKubectlAsync(new[] { "get", "agents", "-A", "-o", "json" });
            if (!result.Success) { _logger.LogWarning("kubectl get agents failed: {Error}", result.Stderr); return new(); }

            var json   = JsonDocument.Parse(result.Stdout);
            var agents = new List<Agent>();
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var metadata = item.GetProperty("metadata");
                var spec     = item.TryGetProperty("spec", out var s) ? s : default;
                var name     = metadata.GetProperty("name").GetString() ?? "";
                var ns       = metadata.GetProperty("namespace").GetString() ?? "kagent";
                string description = "";
                if (spec.ValueKind != JsonValueKind.Undefined)
                {
                    if (spec.TryGetProperty("description", out var desc)) description = desc.GetString() ?? "";
                    else if (spec.TryGetProperty("declarative", out var dec) &&
                             dec.TryGetProperty("a2aConfig", out var a2a) &&
                             a2a.TryGetProperty("skills", out var skills) &&
                             skills.GetArrayLength() > 0 &&
                             skills[0].TryGetProperty("description", out var sd))
                        description = sd.GetString() ?? "";
                }
                agents.Add(new Agent
                {
                    Id = $"{ns}/{name}", Name = name, Type = "KAgent", Status = "Active", Ready = true, Accepted = true,
                    Description = description.Length > 100 ? description[..100] + "..." : description
                });
            }
            return agents;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get agents from Kubernetes"); return new(); }
    }

    public async Task<Agent?> GetByIdAsync(string id)
    {
        try { return await _kagent.GetAgentAsync(id); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get agent {Id}", id); return null; }
    }

    public async Task<Agent> CreateAsync(CreateAgentRequest request)
    {
        _logger.LogInformation("Creating agent {Name} in namespace {Namespace}", request.Name, request.Namespace);
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, BuildAgentYaml(request));
            var result = await _commandService.ExecuteKubectlAsync(new[] { "apply", "-f", tempFile });
            if (!result.Success) throw new Exception($"Failed to create agent: {result.Stderr}");
            _logger.LogInformation("Agent {Name} created successfully", request.Name);
            return new Agent { Id = $"{request.Namespace}/{request.Name}", Name = request.Name, Type = request.Type, Status = "Pending", Description = request.Description };
        }
        finally { if (File.Exists(tempFile)) File.Delete(tempFile); }
    }

    public async Task<bool> UpdateStatusAsync(string id, string status)
    {
        _logger.LogInformation("Updating status for agent {Id} to {Status}", id, status);
        var parts = id.Split('/');
        var ns    = parts.Length > 1 ? parts[0] : "kagent";
        var name  = parts.Length > 1 ? parts[1] : id;
        if (status.ToLower() is "paused" or "inactive")
            return (await _commandService.ExecuteKubectlAsync(new[] { "scale", "deployment", $"{name}-agent", "-n", ns, "--replicas=0" })).Success;
        if (status.ToLower() == "active")
            return (await _commandService.ExecuteKubectlAsync(new[] { "scale", "deployment", $"{name}-agent", "-n", ns, "--replicas=1" })).Success;
        return false;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        _logger.LogInformation("Deleting agent {Id}", id);
        var parts  = id.Split('/');
        var ns     = parts.Length > 1 ? parts[0] : "kagent";
        var name   = parts.Length > 1 ? parts[1] : id;
        var result = await _commandService.ExecuteKubectlAsync(new[] { "delete", "agent", name, "-n", ns });
        if (!result.Success) _logger.LogError("Failed to delete agent: {Error}", result.Stderr);
        return result.Success;
    }

    private string BuildAgentYaml(CreateAgentRequest request)
    {
        var yaml = $@"apiVersion: kagent.dev/v1alpha1
kind: Agent
metadata:
  name: {request.Name}
  namespace: {request.Namespace}
spec:
  description: ""{Esc(request.Description)}""
  modelConfig: ""{request.Namespace}/{request.ModelConfig}""";
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            yaml += $"\n  systemMessage: \"{Esc(request.SystemPrompt)}\"";
        if (request.ToolServers.Any())
        {
            yaml += "\n  tools:";
            foreach (var ts in request.ToolServers) yaml += $"\n    - toolServer: {request.Namespace}/{ts}";
        }
        return yaml.Trim() + "\n";
    }

    private static string Esc(string v) => (v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
