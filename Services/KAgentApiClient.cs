using System.Text;
using System.Text.Json;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class KAgentApiClient
{
    private readonly HttpClient _httpClient;
    private readonly KAgentConfig _config;
    private readonly ILogger<KAgentApiClient> _logger;
    private readonly string _userId;

    public KAgentApiClient(HttpClient httpClient, IConfiguration configuration, ILogger<KAgentApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _userId = Guid.NewGuid().ToString();

        _config = new KAgentConfig();
        configuration.GetSection("KAgent").Bind(_config);

        _httpClient.BaseAddress = new Uri(_config.GetFullUrl());
        _httpClient.Timeout = TimeSpan.FromMilliseconds(_config.Timeout);

        if (!string.IsNullOrEmpty(_config.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.Token);
        }
    }

    private async Task<T> RequestAsync<T>(string endpoint, HttpMethod method, object? body = null)
    {
        var url = $"{endpoint}?user_id={_userId}";

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-User-ID", _userId);

        if (body != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            );
        }

        try
        {
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("KAgent API error: {StatusCode} - {Content}", response.StatusCode, content);
                throw new HttpRequestException($"KAgent API error: {response.StatusCode}");
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // KAgent wraps responses in {"error": false, "data": [...], "message": "..."}
            // Try to parse as wrapped response first
            try
            {
                var wrappedResponse = JsonSerializer.Deserialize<KAgentResponse<T>>(content, options);
                if (wrappedResponse != null && !wrappedResponse.Error && wrappedResponse.Data != null)
                {
                    return wrappedResponse.Data;
                }

                // If wrapped but error=true, throw
                if (wrappedResponse?.Error == true)
                {
                    throw new HttpRequestException($"KAgent API error: {wrappedResponse.Message}");
                }
            }
            catch (JsonException)
            {
                // Not a wrapped response, try direct deserialization
            }

            // Fallback to direct deserialization for non-wrapped responses
            return JsonSerializer.Deserialize<T>(content, options)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call KAgent API: {Endpoint}", endpoint);
            throw;
        }
    }

    public async Task<List<Agent>> GetAgentsAsync()
    {
        try
        {
            var url = $"/api/agents?user_id={_userId}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get agents: {StatusCode} - {Content}", response.StatusCode, content);
                return new List<Agent>();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // KAgent API returns nested structure: { data: [{ agent: { metadata, spec, status }, deploymentReady }] }
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            List<JsonElement> agentsArray = new();

            // Handle different response formats
            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataElement.EnumerateArray())
                    agentsArray.Add(item);
            }
            else if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                    agentsArray.Add(item);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                    agentsArray.Add(item);
            }

            var agents = new List<Agent>();
            foreach (var item in agentsArray)
            {
                try
                {
                    // KAgent nests agent data in "agent" property
                    var agentData = item.TryGetProperty("agent", out var agentProp) ? agentProp : item;
                    var metadata = agentData.TryGetProperty("metadata", out var metaProp) ? metaProp : agentData;
                    var spec = agentData.TryGetProperty("spec", out var specProp) ? specProp : agentData;
                    var status = agentData.TryGetProperty("status", out var statusProp) ? statusProp : agentData;

                    var name = GetStringProperty(metadata, "name") ?? GetStringProperty(agentData, "name") ?? "unknown";
                    var ns = GetStringProperty(metadata, "namespace") ?? "kagent";

                    // Check deployment ready status
                    var isReady = item.TryGetProperty("deploymentReady", out var readyProp) && readyProp.GetBoolean();

                    // Also check status conditions
                    if (!isReady && status.TryGetProperty("conditions", out var conditions))
                    {
                        foreach (var cond in conditions.EnumerateArray())
                        {
                            if (GetStringProperty(cond, "type") == "Ready" && GetStringProperty(cond, "status") == "True")
                            {
                                isReady = true;
                                break;
                            }
                        }
                    }

                    agents.Add(new Agent
                    {
                        Id = GetStringProperty(agentData, "id") ?? name,
                        Name = name,
                        Namespace = ns,
                        Type = GetStringProperty(spec, "type") ?? "Declarative",
                        Description = GetStringProperty(spec, "description") ?? "",
                        Status = isReady ? "Active" : "Inactive"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse agent");
                }
            }

            return agents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get agents from KAgent API");
            throw;
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    public async Task<Agent?> GetAgentAsync(string agentId)
    {
        return await RequestAsync<Agent?>($"/api/agents/{agentId}", HttpMethod.Get);
    }

    public async Task<KAgentSession> CreateSessionAsync(string agentName)
    {
        // KAgent expects agent_ref in format "namespace/agent-name"
        var agentRef = agentName.Contains("/") ? agentName : $"kagent/{agentName}";
        return await RequestAsync<KAgentSession>("/api/sessions", HttpMethod.Post, new
        {
            user_id = _userId,
            agent_ref = agentRef
        });
    }

    public async Task<List<KAgentSession>> GetSessionsAsync()
    {
        return await RequestAsync<List<KAgentSession>>("/api/sessions", HttpMethod.Get);
    }

    public async Task<KAgentSession?> GetSessionAsync(string sessionId)
    {
        return await RequestAsync<KAgentSession?>($"/api/sessions/{sessionId}", HttpMethod.Get);
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await RequestAsync<object>($"/api/sessions/{sessionId}", HttpMethod.Delete);
    }

    public async Task<ChatResponse> SendMessageAsync(string sessionId, string message)
    {
        try
        {
            // First get the session to find agent info
            var session = await GetSessionAsync(sessionId);
            if (session == null)
            {
                throw new HttpRequestException("Session not found");
            }

            // Extract agent namespace and name from agent_id
            var agentNamespace = "kagent";
            var agentName = "k8s-agent";

            if (!string.IsNullOrEmpty(session.AgentId))
            {
                var parts = session.AgentId.Split("__NS__");
                if (parts.Length == 2)
                {
                    agentNamespace = parts[0];
                    agentName = parts[1].Replace("_", "-");
                }
                else
                {
                    agentName = session.AgentId.Replace("_", "-");
                }
            }

            // Use A2A protocol
            var a2aUrl = $"{_config.GetFullUrl()}/api/a2a/{agentNamespace}/{agentName}/";

            var a2aRequest = new
            {
                jsonrpc = "2.0",
                method = "message/stream",
                @params = new
                {
                    message = new
                    {
                        kind = "message",
                        messageId = $"msg-{DateTime.UtcNow.Ticks}",
                        role = "user",
                        parts = new[] { new { kind = "text", text = message } },
                        contextId = sessionId
                    }
                },
                id = $"req-{DateTime.UtcNow.Ticks}"
            };

            var requestContent = new StringContent(
                JsonSerializer.Serialize(a2aRequest),
                Encoding.UTF8,
                "application/json"
            );

            var request = new HttpRequestMessage(HttpMethod.Post, a2aUrl);
            request.Content = requestContent;
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"A2A request failed: {response.StatusCode} - {errorContent}");
            }

            // Read SSE stream
            var responseContent = await response.Content.ReadAsStringAsync();
            var lastMessage = "";

            // Parse SSE events
            var lines = responseContent.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    var dataString = line.Substring(6);
                    if (dataString == "[DONE]") break;

                    try
                    {
                        using var doc = JsonDocument.Parse(dataString);
                        var root = doc.RootElement;

                        // Look for result.status.message
                        if (root.TryGetProperty("result", out var result) &&
                            result.TryGetProperty("status", out var status) &&
                            status.TryGetProperty("message", out var msg))
                        {
                            if (msg.TryGetProperty("role", out var role) && role.GetString() == "agent" &&
                                msg.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                if (parts[0].TryGetProperty("text", out var textProp))
                                {
                                    lastMessage = textProp.GetString() ?? "";
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip invalid JSON
                    }
                }
            }

            if (!string.IsNullOrEmpty(lastMessage))
            {
                return new ChatResponse
                {
                    ConversationId = sessionId,
                    Message = new ChatMessage
                    {
                        Content = lastMessage,
                        Role = "assistant",
                        Timestamp = DateTime.UtcNow
                    }
                };
            }

            throw new HttpRequestException("No response from agent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A2A message failed for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<List<ChatMessage>> GetSessionMessagesAsync(string sessionId)
    {
        return await RequestAsync<List<ChatMessage>>($"/api/sessions/{sessionId}/messages", HttpMethod.Get);
    }

    public async Task<List<KAgentEvent>> GetSessionEventsAsync(string sessionId, int? limit = null)
    {
        var url = $"/api/sessions/{sessionId}";
        if (limit.HasValue)
        {
            url += $"?limit={limit}";
        }
        return await RequestAsync<List<KAgentEvent>>(url, HttpMethod.Get);
    }

    public async Task<List<KAgentTask>> GetSessionTasksAsync(string sessionId)
    {
        return await RequestAsync<List<KAgentTask>>($"/api/sessions/{sessionId}/tasks", HttpMethod.Get);
    }

    public async Task<List<ToolServer>> GetToolServersAsync()
    {
        return await RequestAsync<List<ToolServer>>("/api/toolservers", HttpMethod.Get);
    }

    public async Task<ToolServer> CreateToolServerAsync(CreateToolServerRequest request)
    {
        return await RequestAsync<ToolServer>("/api/toolservers", HttpMethod.Post, request);
    }

    public async Task DeleteToolServerAsync(string ns, string name)
    {
        await RequestAsync<object>($"/api/toolservers/{ns}/{name}", HttpMethod.Delete);
    }

    public async Task<object> InvokeToolAsync(string serverRef, string toolName, object parameters)
    {
        return await RequestAsync<object>("/api/tools/invoke", HttpMethod.Post, new
        {
            server_ref = serverRef,
            tool_name = toolName,
            parameters = parameters
        });
    }

    public async Task<List<MemoryItem>> GetMemoriesAsync()
    {
        return await RequestAsync<List<MemoryItem>>("/api/memories", HttpMethod.Get);
    }

    public async Task<MemoryItem> CreateMemoryAsync(CreateMemoryRequest request)
    {
        return await RequestAsync<MemoryItem>("/api/memories", HttpMethod.Post, request);
    }

    public async Task<List<MemoryItem>> SearchMemoryAsync(string query)
    {
        return await RequestAsync<List<MemoryItem>>("/api/memories/search", HttpMethod.Post, new { query });
    }

    public async Task<List<Hook>> GetHooksAsync()
    {
        return await RequestAsync<List<Hook>>("/api/hooks", HttpMethod.Get);
    }

    public async Task<Hook> CreateHookAsync(Hook hook)
    {
        return await RequestAsync<Hook>("/api/hooks", HttpMethod.Post, hook);
    }

    public async Task<bool> EnableHookAsync(string hookId)
    {
        await RequestAsync<object>($"/api/hooks/{hookId}/enable", HttpMethod.Post);
        return true;
    }

    public async Task<bool> DisableHookAsync(string hookId)
    {
        await RequestAsync<object>($"/api/hooks/{hookId}/disable", HttpMethod.Post);
        return true;
    }

    public async Task<List<Alert>> GetAlertsAsync()
    {
        return await RequestAsync<List<Alert>>("/api/alerts", HttpMethod.Get);
    }

    public async Task<AlertSummary> GetAlertSummaryAsync()
    {
        try
        {
            return await RequestAsync<AlertSummary>("/api/alerts/summary", HttpMethod.Get);
        }
        catch
        {
            return new AlertSummary();
        }
    }

    public async Task<bool> AcknowledgeAlertAsync(string alertId)
    {
        await RequestAsync<object>($"/api/alerts/{alertId}/acknowledge", HttpMethod.Post);
        return true;
    }

    public async Task<bool> ResolveAlertAsync(string alertId)
    {
        await RequestAsync<object>($"/api/alerts/{alertId}/resolve", HttpMethod.Post);
        return true;
    }
}
