using SkanyxxWeb.Interfaces;
using System.Text.Json;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

/// <summary>
/// Real implementations using KAgent API and command execution
/// </summary>

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
        // Try KAgent API first
        try
        {
            var agents = await _kagent.GetAgentsAsync();
            if (agents.Count > 0) return agents;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KAgent API failed, falling back to Kubernetes CRD");
        }

        // Fallback: Get agents from Kubernetes CRD directly
        return await GetAgentsFromKubernetesAsync();
    }

    private async Task<List<Agent>> GetAgentsFromKubernetesAsync()
    {
        try
        {
            var result = await _commandService.ExecuteKubectlAsync(new[]
            {
                "get", "agents", "-A", "-o", "json"
            });

            if (!result.Success)
            {
                _logger.LogWarning("kubectl get agents failed: {Error}", result.Stderr);
                return new List<Agent>();
            }

            var json = JsonDocument.Parse(result.Stdout);
            var agents = new List<Agent>();

            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var metadata = item.GetProperty("metadata");
                var spec = item.TryGetProperty("spec", out var s) ? s : default;

                var name = metadata.GetProperty("name").GetString() ?? "";
                var ns = metadata.GetProperty("namespace").GetString() ?? "kagent";

                // Get description from spec if available
                string description = "";
                if (spec.ValueKind != JsonValueKind.Undefined)
                {
                    if (spec.TryGetProperty("description", out var desc))
                    {
                        description = desc.GetString() ?? "";
                    }
                    else if (spec.TryGetProperty("declarative", out var declarative) &&
                             declarative.TryGetProperty("a2aConfig", out var a2a) &&
                             a2a.TryGetProperty("skills", out var skills) &&
                             skills.GetArrayLength() > 0)
                    {
                        var firstSkill = skills[0];
                        if (firstSkill.TryGetProperty("description", out var skillDesc))
                        {
                            description = skillDesc.GetString() ?? "";
                        }
                    }
                }

                agents.Add(new Agent
                {
                    Id = $"{ns}/{name}",
                    Name = name,
                    Type = "KAgent",
                    Status = "Active",
                    Description = description.Length > 100 ? description[..100] + "..." : description
                });
            }

            return agents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get agents from Kubernetes");
            return new List<Agent>();
        }
    }

    public async Task<Agent?> GetByIdAsync(string id)
    {
        try
        {
            return await _kagent.GetAgentAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get agent {Id}", id);
            return null;
        }
    }

    public async Task<Agent> CreateAsync(CreateAgentRequest request)
    {
        _logger.LogInformation("Creating agent {Name} in namespace {Namespace}", request.Name, request.Namespace);

        // Build the Agent CRD YAML
        var agentYaml = BuildAgentYaml(request);

        // Create a temp file for the YAML
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, agentYaml);

            // Apply the YAML using kubectl
            var result = await _commandService.ExecuteKubectlAsync(new[] { "apply", "-f", tempFile });

            if (!result.Success)
            {
                _logger.LogError("Failed to create agent: {Error}", result.Stderr);
                throw new Exception($"Failed to create agent: {result.Stderr}");
            }

            _logger.LogInformation("Agent {Name} created successfully", request.Name);

            // Return the created agent
            return new Agent
            {
                Id = $"{request.Namespace}/{request.Name}",
                Name = request.Name,
                Type = request.Type,
                Status = "Pending",
                Description = request.Description
            };
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public async Task<bool> UpdateStatusAsync(string id, string status)
    {
        _logger.LogInformation("Updating status for agent {Id} to {Status}", id, status);

        // Parse namespace/name from id
        var parts = id.Split('/');
        var ns = parts.Length > 1 ? parts[0] : "kagent";
        var name = parts.Length > 1 ? parts[1] : id;

        // For status updates, we can scale the deployment
        if (status.ToLower() == "paused" || status.ToLower() == "inactive")
        {
            var result = await _commandService.ExecuteKubectlAsync(new[]
            {
                "scale", "deployment", $"{name}-agent", "-n", ns, "--replicas=0"
            });
            return result.Success;
        }
        else if (status.ToLower() == "active")
        {
            var result = await _commandService.ExecuteKubectlAsync(new[]
            {
                "scale", "deployment", $"{name}-agent", "-n", ns, "--replicas=1"
            });
            return result.Success;
        }

        return false;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        _logger.LogInformation("Deleting agent {Id}", id);

        // Parse namespace/name from id
        var parts = id.Split('/');
        var ns = parts.Length > 1 ? parts[0] : "kagent";
        var name = parts.Length > 1 ? parts[1] : id;

        var result = await _commandService.ExecuteKubectlAsync(new[]
        {
            "delete", "agent", name, "-n", ns
        });

        if (!result.Success)
        {
            _logger.LogError("Failed to delete agent: {Error}", result.Stderr);
        }

        return result.Success;
    }

    private string BuildAgentYaml(CreateAgentRequest request)
    {
        // Build YAML using correct KAgent CRD schema
        var yaml = $@"apiVersion: kagent.dev/v1alpha1
kind: Agent
metadata:
  name: {request.Name}
  namespace: {request.Namespace}
spec:
  description: ""{EscapeYamlString(request.Description)}""
  modelConfig: ""{request.Namespace}/{request.ModelConfig}""";

        // Add system message if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            yaml += $@"
  systemMessage: ""{EscapeYamlString(request.SystemPrompt)}""";
        }

        // Add tools (tool servers) if provided
        if (request.ToolServers.Any())
        {
            yaml += @"
  tools:";
            foreach (var ts in request.ToolServers)
            {
                yaml += $@"
    - toolServer: {request.Namespace}/{ts}";
            }
        }

        return yaml.Trim() + "\n";
    }

    private string EscapeYamlString(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}

public class KAgentChatService : IChatService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentChatService> _logger;

    public KAgentChatService(KAgentApiClient kagent, ILogger<KAgentChatService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<ChatResponse> SendMessageAsync(ChatRequest request)
    {
        try
        {
            // Create session if needed
            var sessionId = request.ConversationId;
            if (string.IsNullOrEmpty(sessionId))
            {
                var agentName = string.IsNullOrEmpty(request.AgentName) ? "default-agent" : request.AgentName;
                var session = await _kagent.CreateSessionAsync(agentName);
                sessionId = session.Id;
            }

            return await _kagent.SendMessageAsync(sessionId, request.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to agent {Agent}", request.AgentName);
            throw;
        }
    }

    public async Task<List<ChatMessage>> GetConversationAsync(string conversationId)
    {
        try
        {
            return await _kagent.GetSessionMessagesAsync(conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get conversation {Id}", conversationId);
            return new List<ChatMessage>();
        }
    }

    public async Task DeleteConversationAsync(string conversationId)
    {
        try
        {
            await _kagent.DeleteSessionAsync(conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete conversation {Id}", conversationId);
        }
    }
}

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
        // Get deployments with agent label
        var result = await _commandService.ExecuteKubectlAsync(new[]
        {
            "get", "deployments", "-A",
            "-l", "app.kubernetes.io/component=agent",
            "-o", "json"
        });

        if (!result.Success)
        {
            _logger.LogWarning("kubectl failed: {Error}", result.Stderr);
            return new List<Agent>();
        }

        try
        {
            var json = JsonDocument.Parse(result.Stdout);
            var agents = new List<Agent>();

            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var metadata = item.GetProperty("metadata");
                var status = item.GetProperty("status");

                agents.Add(new Agent
                {
                    Id = metadata.GetProperty("uid").GetString() ?? "",
                    Name = metadata.GetProperty("name").GetString() ?? "",
                    Type = metadata.TryGetProperty("labels", out var labels) &&
                           labels.TryGetProperty("app.kubernetes.io/component", out var comp)
                           ? comp.GetString() ?? "Unknown" : "Unknown",
                    Status = status.TryGetProperty("availableReplicas", out var replicas) &&
                             replicas.GetInt32() > 0 ? "Active" : "Inactive"
                });
            }

            return agents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse kubectl output");
            return new List<Agent>();
        }
    }

    public async Task<List<SystemStatus>> GetClusterStatusAsync()
    {
        var statuses = new List<SystemStatus>();

        // Check API server
        var apiResult = await _commandService.ExecuteKubectlAsync(new[] { "cluster-info" });
        statuses.Add(new SystemStatus
        {
            ServiceName = "Kubernetes API",
            Status = apiResult.Success ? "Online" : "Offline",
            LatencyMs = 0
        });

        // Get nodes status
        var nodesResult = await _commandService.ExecuteKubectlAsync(new[] { "get", "nodes", "-o", "json" });
        if (nodesResult.Success)
        {
            try
            {
                var json = JsonDocument.Parse(nodesResult.Stdout);
                foreach (var node in json.RootElement.GetProperty("items").EnumerateArray())
                {
                    var name = node.GetProperty("metadata").GetProperty("name").GetString();
                    var conditions = node.GetProperty("status").GetProperty("conditions").EnumerateArray();
                    var ready = conditions.Any(c =>
                        c.GetProperty("type").GetString() == "Ready" &&
                        c.GetProperty("status").GetString() == "True");

                    statuses.Add(new SystemStatus
                    {
                        ServiceName = $"Node: {name}",
                        Status = ready ? "Online" : "NotReady",
                        LatencyMs = 0
                    });
                }
            }
            catch { }
        }

        return statuses;
    }

    public async Task<object> GetPodsAsync(string? ns = null)
    {
        var args = new List<string> { "get", "pods", "-o", "json" };
        if (!string.IsNullOrEmpty(ns))
        {
            args.AddRange(new[] { "-n", ns });
        }
        else
        {
            args.Add("-A");
        }

        var result = await _commandService.ExecuteKubectlAsync(args.ToArray());
        if (!result.Success)
        {
            throw new Exception($"kubectl failed: {result.Stderr}");
        }

        return JsonDocument.Parse(result.Stdout).RootElement;
    }

    public async Task<object> GetDeploymentsAsync(string? ns = null)
    {
        var args = new List<string> { "get", "deployments", "-o", "json" };
        if (!string.IsNullOrEmpty(ns))
        {
            args.AddRange(new[] { "-n", ns });
        }
        else
        {
            args.Add("-A");
        }

        var result = await _commandService.ExecuteKubectlAsync(args.ToArray());
        if (!result.Success)
        {
            throw new Exception($"kubectl failed: {result.Stderr}");
        }

        return JsonDocument.Parse(result.Stdout).RootElement;
    }

    public async Task<object> GetServicesAsync(string? ns = null)
    {
        var args = new List<string> { "get", "services", "-o", "json" };
        if (!string.IsNullOrEmpty(ns))
        {
            args.AddRange(new[] { "-n", ns });
        }
        else
        {
            args.Add("-A");
        }

        var result = await _commandService.ExecuteKubectlAsync(args.ToArray());
        if (!result.Success)
        {
            throw new Exception($"kubectl failed: {result.Stderr}");
        }

        return JsonDocument.Parse(result.Stdout).RootElement;
    }

    public async Task<bool> ScaleDeploymentAsync(string name, string ns, int replicas)
    {
        var result = await _commandService.ExecuteKubectlAsync(new[]
        {
            "scale", "deployment", name, "-n", ns, $"--replicas={replicas}"
        });

        return result.Success;
    }

    public async Task<bool> RestartDeploymentAsync(string name, string ns)
    {
        var result = await _commandService.ExecuteKubectlAsync(new[]
        {
            "rollout", "restart", "deployment", name, "-n", ns
        });

        return result.Success;
    }
}

public class KAgentToolServerService : IToolServerService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentToolServerService> _logger;

    public KAgentToolServerService(KAgentApiClient kagent, ILogger<KAgentToolServerService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<List<ToolServer>> GetAllAsync()
    {
        try
        {
            return await _kagent.GetToolServersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tool servers");
            return new List<ToolServer>();
        }
    }

    public Task<ToolServer?> GetByIdAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<bool> PingAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<bool> RestartAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<List<ToolCall>> GetRecentCallsAsync(int limit = 10)
    {
        return Task.FromResult(new List<ToolCall>());
    }

    public async Task<object> InvokeToolAsync(string serverId, string toolName, object parameters)
    {
        return await _kagent.InvokeToolAsync(serverId, toolName, parameters);
    }
}

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

public class KAgentAlertService : IAlertService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentAlertService> _logger;

    public KAgentAlertService(KAgentApiClient kagent, ILogger<KAgentAlertService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<List<Alert>> GetAllAsync(string? severity = null, string? status = null)
    {
        try
        {
            var alerts = await _kagent.GetAlertsAsync();
            if (!string.IsNullOrEmpty(severity))
            {
                alerts = alerts.Where(a => a.Severity == severity).ToList();
            }
            if (!string.IsNullOrEmpty(status))
            {
                alerts = alerts.Where(a => a.Status == status).ToList();
            }
            return alerts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get alerts");
            return new List<Alert>();
        }
    }

    public Task<Alert?> GetByIdAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<Alert> CreateAsync(Alert alert)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> AcknowledgeAsync(string id)
    {
        return await _kagent.AcknowledgeAlertAsync(id);
    }

    public async Task<bool> ResolveAsync(string id)
    {
        return await _kagent.ResolveAlertAsync(id);
    }

    public async Task<object> GetStatsAsync()
    {
        var alerts = await GetAllAsync();
        return new
        {
            Critical = alerts.Count(a => a.Severity == "Critical" && a.Status == "Active"),
            Warning = alerts.Count(a => a.Severity == "Warning" && a.Status == "Active"),
            Info = alerts.Count(a => a.Severity == "Info" && a.Status == "Active"),
            ResolvedToday = alerts.Count(a => a.Status == "Resolved" && a.ResolvedAt?.Date == DateTime.UtcNow.Date)
        };
    }
}

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

    public Task<bool> DeleteAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<object> TestAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<List<HookExecution>> GetExecutionsAsync(int limit = 10)
    {
        return Task.FromResult(new List<HookExecution>());
    }
}

public class CloudCommandService : ICloudProviderService
{
    private readonly ICommandExecutionService _commandService;
    private readonly ILogger<CloudCommandService> _logger;

    public CloudCommandService(ICommandExecutionService commandService, ILogger<CloudCommandService> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    public async Task<List<CloudProvider>> GetProvidersAsync()
    {
        var providers = new List<CloudProvider>();

        // Check AWS
        var awsInfo = await _commandService.CheckToolAvailabilityAsync("aws");
        if (awsInfo.Available)
        {
            var awsResult = await _commandService.ExecuteAwsAsync(new[] { "sts", "get-caller-identity", "--output", "json" });
            providers.Add(new CloudProvider
            {
                Id = "aws",
                Name = "AWS",
                Type = "AWS",
                Status = awsResult.Success ? "Connected" : "NotAuthenticated"
            });
        }

        // Check Azure
        var azInfo = await _commandService.CheckToolAvailabilityAsync("az");
        if (azInfo.Available)
        {
            var azResult = await _commandService.ExecuteAzAsync(new[] { "account", "show", "--output", "json" });
            providers.Add(new CloudProvider
            {
                Id = "azure",
                Name = "Azure",
                Type = "Azure",
                Status = azResult.Success ? "Connected" : "NotAuthenticated"
            });
        }

        // Check GCP
        var gcloudInfo = await _commandService.CheckToolAvailabilityAsync("gcloud");
        if (gcloudInfo.Available)
        {
            var gcpResult = await _commandService.ExecuteAsync("gcloud", new[] { "auth", "list", "--format=json" });
            providers.Add(new CloudProvider
            {
                Id = "gcp",
                Name = "GCP",
                Type = "GCP",
                Status = gcpResult.Success ? "Connected" : "NotAuthenticated"
            });
        }

        return providers;
    }

    public Task<CloudProvider?> GetProviderAsync(string id)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ConnectAsync(string id, object credentials)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DisconnectAsync(string id)
    {
        throw new NotImplementedException();
    }

    public async Task<Dictionary<string, int>> GetResourceCountsAsync(string providerId)
    {
        var counts = new Dictionary<string, int>();

        if (providerId == "aws")
        {
            // Get EC2 instances count
            var ec2Result = await _commandService.ExecuteAwsAsync(new[]
            {
                "ec2", "describe-instances", "--query", "Reservations[*].Instances[*].[InstanceId]", "--output", "json"
            });
            if (ec2Result.Success)
            {
                try
                {
                    var json = JsonDocument.Parse(ec2Result.Stdout);
                    counts["EC2 Instances"] = json.RootElement.EnumerateArray()
                        .SelectMany(r => r.EnumerateArray())
                        .Count();
                }
                catch { }
            }
        }

        return counts;
    }
}
