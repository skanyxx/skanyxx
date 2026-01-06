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
        return await RequestAsync<List<Agent>>("/api/agents", HttpMethod.Get);
    }

    public async Task<Agent?> GetAgentAsync(string agentId)
    {
        return await RequestAsync<Agent?>($"/api/agents/{agentId}", HttpMethod.Get);
    }

    public async Task<KAgentSession> CreateSessionAsync(string agentName)
    {
        return await RequestAsync<KAgentSession>("/api/sessions", HttpMethod.Post, new { agent_name = agentName });
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
        return await RequestAsync<ChatResponse>("/api/sendmessage", HttpMethod.Post, new
        {
            message = message,
            session = new { id = sessionId }
        });
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
