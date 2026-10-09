using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.Services;

/// <summary>
/// kagent's controller API. Every call has its own deadline (the HttpClient has none): control calls
/// <see cref="KAgentConfig.ControlTimeoutSeconds"/>, a chat turn <see cref="KAgentConfig.ChatTimeoutSeconds"/>; a missed
/// deadline is a <see cref="TimeoutException"/>. kagent refusing or answering garbage is an
/// <see cref="HttpRequestException"/> with a fixed message (and the status, when there is one); kagent's own text goes to
/// the log only. No call is ever retried (see <c>AddKAgentHttpClient</c>).
/// </summary>
public class KAgentApiClient
{
    public const string HttpClientName = "kagent";

    private readonly HttpClient _httpClient;
    private readonly KAgentConfig _config;
    private readonly ILogger<KAgentApiClient> _logger;
    private readonly string _userId;
    private readonly TimeSpan _controlTimeout;
    private readonly TimeSpan _chatTimeout;

    public KAgentApiClient(HttpClient httpClient, IConfiguration configuration, ILogger<KAgentApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _userId = Guid.NewGuid().ToString();

        _config = new KAgentConfig();
        configuration.GetSection("KAgent").Bind(_config);
        _controlTimeout = TimeSpan.FromSeconds(_config.ControlTimeoutSeconds);
        _chatTimeout = TimeSpan.FromSeconds(_config.ChatTimeoutSeconds);

        _httpClient.BaseAddress = new Uri(_config.GetFullUrl());

        if (!string.IsNullOrEmpty(_config.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.Token);
        }
    }

    /// <summary>The ModelConfig the model step edits, <c>namespace/name</c> (<c>KAgent:ModelConfig</c>).</summary>
    public string ModelConfigRef => _config.ModelConfig;

    // kagent (auth mode unsecure) takes the user from ?user_id=, else X-User-Id. Without a userId, every call is this
    // process's own anonymous user (the legacy console pages); Chat passes the signed-in person (D5).
    private string WithUser(string endpoint, string? userId) =>
        $"{endpoint}{(endpoint.Contains('?') ? '&' : '?')}user_id={Uri.EscapeDataString(userId ?? _userId)}";

    /// <summary>Sends with a deadline of its own; the body is buffered before the deadline ends.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            return await _httpClient.SendAsync(request, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"kagent did not answer within {timeout.TotalSeconds:0} s.");
        }
    }

    private async Task<T> RequestAsync<T>(string endpoint, HttpMethod method, object? body = null, string? userId = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, WithUser(endpoint, userId));
        request.Headers.Add("X-User-ID", userId ?? _userId);

        if (body != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            );
        }

        using var response = await SendAsync(request, _controlTimeout, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Warning, not Error: a 404 is routine (another person's conversation is "not found" to this one).
            _logger.LogWarning("kagent {Method} {Endpoint} answered {StatusCode}: {Content}", method, endpoint, (int)response.StatusCode, Cut(content));
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        try
        {
            // KAgent wraps responses in {"error": false, "data": [...], "message": "..."}
            try
            {
                var wrappedResponse = JsonSerializer.Deserialize<KAgentResponse<T>>(content, options);
                if (wrappedResponse != null && !wrappedResponse.Error && wrappedResponse.Data != null)
                    return wrappedResponse.Data;

                if (wrappedResponse?.Error == true)
                {
                    _logger.LogWarning("kagent {Method} {Endpoint} refused: {Message}", method, endpoint, Cut(wrappedResponse.Message ?? ""));
                    throw new HttpRequestException("kagent refused the request", null, response.StatusCode);
                }
            }
            catch (JsonException)
            {
                // Not a wrapped response, try direct deserialization
            }

            return JsonSerializer.Deserialize<T>(content, options)!;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "kagent {Method} {Endpoint} answered in an unexpected shape", method, endpoint);
            throw new HttpRequestException("kagent answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }
    }

    /// <summary>Every agent kagent lists. kagent failing (non-2xx, garbage) throws; it is never "no agents".</summary>
    public async Task<List<Agent>> GetAgentsAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithUser("/api/agents", null));
        using var response = await SendAsync(request, _controlTimeout, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("kagent GET /api/agents answered {StatusCode}: {Content}", (int)response.StatusCode, Cut(content));
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "kagent GET /api/agents answered invalid JSON");
            throw new HttpRequestException("kagent answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }

        using (doc)
        {
            var root = doc.RootElement;

            // KAgent API returns nested structure: { data: [{ agent: { metadata, spec, status }, deploymentReady }] }
            List<JsonElement> agentsArray = new();
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                agentsArray.AddRange(dataElement.EnumerateArray());
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                agentsArray.AddRange(itemsElement.EnumerateArray());
            else if (root.ValueKind == JsonValueKind.Array)
                agentsArray.AddRange(root.EnumerateArray());
            else
            {
                _logger.LogWarning("kagent GET /api/agents answered without an agent list");
                throw new HttpRequestException("kagent answered in an unexpected shape", null, HttpStatusCode.BadGateway);
            }

            var agents = new List<Agent>();
            foreach (var item in agentsArray)
            {
                try
                {
                    agents.Add(ParseAgent(item));
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse agent");
                }
            }
            return agents;
        }
    }

    private static Agent ParseAgent(JsonElement item)
    {
        // KAgent nests agent data in "agent" property
        var agentData = item.TryGetProperty("agent", out var agentProp) ? agentProp : item;
        var metadata = agentData.TryGetProperty("metadata", out var metaProp) ? metaProp : agentData;
        var spec = agentData.TryGetProperty("spec", out var specProp) ? specProp : agentData;
        var status = agentData.TryGetProperty("status", out var statusProp) ? statusProp : agentData;

        var name = GetStringProperty(metadata, "name") ?? GetStringProperty(agentData, "name") ?? "unknown";
        var ns = GetStringProperty(metadata, "namespace") ?? "kagent";

        var isReady = item.TryGetProperty("deploymentReady", out var readyProp) && readyProp.ValueKind == JsonValueKind.True
            || HasCondition(status, "Ready");
        var isAccepted = HasCondition(status, "Accepted");

        var labels = new Dictionary<string, string>();
        if (metadata.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Object)
            foreach (var label in labelsProp.EnumerateObject())
                if (label.Value.ValueKind == JsonValueKind.String)
                    labels[label.Name] = label.Value.GetString()!;

        return new Agent
        {
            Id = GetStringProperty(agentData, "id") ?? name,
            Name = name,
            Namespace = ns,
            Type = GetStringProperty(spec, "type") ?? "Declarative",
            Description = GetStringProperty(spec, "description") ?? "",
            Status = isReady ? "Active" : "Inactive",
            Ready = isReady,
            Accepted = isAccepted,
            Labels = labels
        };
    }

    private static bool HasCondition(JsonElement status, string type) =>
        status.ValueKind == JsonValueKind.Object
        && status.TryGetProperty("conditions", out var conditions)
        && conditions.ValueKind == JsonValueKind.Array
        && conditions.EnumerateArray().Any(c => GetStringProperty(c, "type") == type && GetStringProperty(c, "status") == "True");

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    public async Task<Agent?> GetAgentAsync(string agentId)
    {
        return await RequestAsync<Agent?>($"/api/agents/{agentId}", HttpMethod.Get);
    }

    public async Task<KAgentSession> CreateSessionAsync(string agentName, string? userId = null, CancellationToken ct = default)
    {
        // KAgent expects agent_ref in format "namespace/agent-name"
        var agentRef = agentName.Contains("/") ? agentName : $"kagent/{agentName}";
        return await RequestAsync<KAgentSession>("/api/sessions", HttpMethod.Post, new
        {
            user_id = userId ?? _userId,
            agent_ref = agentRef
        }, userId, ct);
    }

    /// <summary><paramref name="userId"/>'s sessions (kagent lists per user); null = this process's own user.</summary>
    public async Task<List<KAgentSession>> GetSessionsAsync(string? userId = null)
    {
        return await RequestAsync<List<KAgentSession>>("/api/sessions", HttpMethod.Get, null, userId);
    }

    /// <summary>The session if it belongs to <paramref name="userId"/> (kagent looks it up by id and user), else null.</summary>
    public async Task<KAgentSession?> GetSessionAsync(string sessionId, string? userId = null, CancellationToken ct = default)
    {
        try
        {
            var response = await RequestAsync<SessionWithEventsResponse>($"/api/sessions/{Uri.EscapeDataString(sessionId)}", HttpMethod.Get, null, userId, ct);
            return response.Session;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteSessionAsync(string sessionId, string? userId = null, CancellationToken ct = default)
    {
        await RequestAsync<object>($"/api/sessions/{Uri.EscapeDataString(sessionId)}", HttpMethod.Delete, null, userId, ct);
    }

    /// <summary>
    /// One blocking A2A <c>message/send</c> to <c>{kagent}/api/a2a/{ns}/{name}/</c> in the session
    /// <paramref name="sessionId"/> (its <c>contextId</c>), as <paramref name="userId"/>: kagent forwards that user to the
    /// agent as <c>X-User-Id</c>, which the seed passes on to the memory MCP (D084). The answer is the completed task's
    /// last artifact (reasoning parts skipped). Bounded by <see cref="KAgentConfig.ChatTimeoutSeconds"/>.
    /// </summary>
    public async Task<ChatResponse> SendMessageAsync(
        string agentNamespace, string agentName, string sessionId, string message, string userId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            WithUser($"/api/a2a/{Uri.EscapeDataString(agentNamespace)}/{Uri.EscapeDataString(agentName)}/", userId))
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString(),
                method = "message/send",
                @params = new
                {
                    message = new
                    {
                        kind = "message",
                        messageId = Guid.NewGuid().ToString(),
                        role = "user",
                        parts = new[] { new { kind = "text", text = message } },
                        contextId = sessionId
                    },
                    configuration = new { blocking = true }
                }
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("A2A-Version", "0.3");
        request.Headers.Add("X-User-Id", userId);

        using var response = await SendAsync(request, _chatTimeout, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("kagent A2A {Namespace}/{Agent} answered {StatusCode}: {Content}",
                agentNamespace, agentName, (int)response.StatusCode, Cut(await response.Content.ReadAsStringAsync(ct)));
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }

        string answer;
        try
        {
            answer = Answer(await response.Content.ReadFromJsonAsync<JsonNode>(ct));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Invalid JSON, or JSON of another shape (an indexer or GetValue on the wrong node kind).
            _logger.LogWarning(ex, "kagent A2A {Namespace}/{Agent} answered in an unexpected shape", agentNamespace, agentName);
            throw new HttpRequestException("kagent answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }

        return new ChatResponse
        {
            ConversationId = sessionId,
            Message = new ChatMessage { Content = answer, Role = "assistant", SessionId = sessionId, Timestamp = DateTime.UtcNow }
        };
    }

    private string Answer(JsonNode? body)
    {
        if (body?["error"] is { } error)
        {
            _logger.LogWarning("kagent JSON-RPC error: {Error}", Cut(error.ToJsonString()));
            throw new HttpRequestException("kagent refused the message");
        }
        var result = body?["result"] ?? throw new HttpRequestException("kagent answered without a result");
        string answer;
        if (result["kind"]?.GetValue<string>() == "message")
            answer = Text(result["parts"]);
        else
        {
            var state = result["status"]?["state"]?.GetValue<string>();
            if (state != "completed")
            {
                _logger.LogWarning("kagent task ended {State}: {Detail}", Cut(state ?? "?"), Cut(Text(result["status"]?["message"]?["parts"])));
                throw new HttpRequestException("the agent did not complete the answer");
            }
            var artifacts = result["artifacts"]?.AsArray();
            answer = artifacts is { Count: > 0 } ? Text(artifacts[^1]!["parts"]) : "";
            if (answer.Length == 0)
                answer = Text(result["status"]?["message"]?["parts"]);
        }
        if (answer.Trim().Length == 0)
            throw new HttpRequestException("the agent answered with no text");
        return answer;
    }

    // kagent's Go runtime marks reasoning parts adk_thought, its Python runtime kagent_thought.
    private static string Text(JsonNode? parts) =>
        parts is not JsonArray array
            ? ""
            : string.Concat(array
                .Where(p => p?["kind"]?.GetValue<string>() == "text"
                    && !(p["metadata"] is JsonObject m && (m["adk_thought"]?.GetValue<bool>() == true || m["kagent_thought"]?.GetValue<bool>() == true)))
                .Select(p => p!["text"]?.GetValue<string>() ?? ""));

    private static string Cut(string text) => text.Length <= 1_000 ? text : text[..1_000];

    /// <summary>The ModelConfig <see cref="ModelConfigRef"/> as kagent returns it (<c>{ref, spec, status}</c>), or null when absent.</summary>
    public async Task<JsonObject?> GetModelConfigAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithUser($"/api/modelconfigs/{_config.ModelConfig}", null));
        using var response = await SendAsync(request, _controlTimeout, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return await ModelConfigDataAsync(response, ct);
    }

    /// <summary>
    /// Creates (<paramref name="create"/>) or replaces the spec of <see cref="ModelConfigRef"/>. A non-empty
    /// <paramref name="apiKey"/> goes inline: kagent writes it into a Secret named like the ModelConfig and points the spec
    /// at it (only when the spec names no secret). The key is never logged and never comes back.
    /// </summary>
    public async Task<JsonObject> SaveModelConfigAsync(bool create, JsonObject spec, string? apiKey, CancellationToken ct)
    {
        var body = new JsonObject { ["spec"] = spec.DeepClone() };
        if (create)
            body["ref"] = _config.ModelConfig;
        if (!string.IsNullOrEmpty(apiKey))
            body["apiKey"] = apiKey;
        using var request = new HttpRequestMessage(create ? HttpMethod.Post : HttpMethod.Put,
            WithUser(create ? "/api/modelconfigs" : $"/api/modelconfigs/{_config.ModelConfig}", null))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var response = await SendAsync(request, _controlTimeout, ct);
        return await ModelConfigDataAsync(response, ct);
    }

    private static async Task<JsonObject> ModelConfigDataAsync(HttpResponseMessage response, CancellationToken ct)
    {
        JsonNode? body = null;
        try
        {
            body = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
        }
        var envelope = body as JsonObject;
        if (!response.IsSuccessStatusCode)
        {
            // kagent's message names what failed (never the key: the request's key goes into a Secret, not the spec).
            // Owner-only path, so the text is shown to the owner.
            var detail = envelope?["message"] is JsonValue m && m.TryGetValue<string>(out var text) ? $": {Cut(text)}" : "";
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}{detail}", null, response.StatusCode);
        }
        return envelope?["data"] as JsonObject
            ?? throw new HttpRequestException("kagent answered in an unexpected shape", null, HttpStatusCode.BadGateway);
    }

    /// <summary>The Agent CR <c>{apiVersion, kind, metadata, spec, status}</c>, or null when kagent has none by that name.</summary>
    public async Task<JsonObject?> GetAgentObjectAsync(string ns, string name, CancellationToken ct)
    {
        var data = await ControlAsync(HttpMethod.Get, $"/api/agents/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}", null, ct, notFoundIsNull: true);
        return data is null ? null : data["agent"] as JsonObject
            ?? throw new HttpRequestException("kagent answered in an unexpected shape", null, HttpStatusCode.BadGateway);
    }

    /// <summary>Creates the Agent CR as given, labels included (kagent keeps them; an update never changes them).</summary>
    public Task CreateAgentAsync(JsonObject agent, CancellationToken ct) => ControlAsync(HttpMethod.Post, "/api/agents", agent, ct);

    /// <summary>Replaces the Agent's spec; kagent ignores the body's metadata except its name and namespace.</summary>
    public Task UpdateAgentAsync(JsonObject agent, CancellationToken ct) => ControlAsync(HttpMethod.Put, "/api/agents", agent, ct);

    /// <returns>False when kagent had no such agent.</returns>
    public async Task<bool> DeleteAgentAsync(string ns, string name, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Delete, $"/api/agents/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}", null, ct, notFoundIsNull: true) is not null;

    /// <summary>kagent's tool servers: <c>[{ref: "ns/name", groupKind, discoveredTools: [{name, description}]}]</c>.</summary>
    public async Task<JsonArray> GetToolServerListAsync(CancellationToken ct) =>
        await ControlAsync(HttpMethod.Get, "/api/toolservers", null, ct) as JsonArray
        ?? throw new HttpRequestException("kagent answered in an unexpected shape", null, HttpStatusCode.BadGateway);

    /// <summary>
    /// Creates a RemoteMCPServer and, owned by it, one Opaque Secret per entry of <paramref name="secrets"/> (kagent's
    /// companion Secrets: garbage-collected with the server). The values are never logged.
    /// </summary>
    public Task CreateRemoteMcpServerAsync(JsonObject server, IReadOnlyList<(string Name, string Key, string Value)> secrets, CancellationToken ct) =>
        ControlAsync(HttpMethod.Post, "/api/toolservers", new JsonObject
        {
            ["type"] = "RemoteMCPServer",
            ["remoteMCPServer"] = server.DeepClone(),
            ["secrets"] = new JsonArray([.. secrets.Select(s => (JsonNode)new JsonObject { ["name"] = s.Name, ["key"] = s.Key, ["value"] = s.Value })])
        }, ct);

    /// <returns>False when kagent had no such tool server (kagent looks it up in its database, so one it never reconciled is absent too).</returns>
    public async Task<bool> DeleteRemoteMcpServerAsync(string ns, string name, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Delete, $"/api/toolservers/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}", null, ct, notFoundIsNull: true) is not null;

    /// <summary>The refs (<c>namespace/name</c>) of every ModelConfig kagent has.</summary>
    public async Task<IReadOnlyList<string>> GetModelConfigRefsAsync(CancellationToken ct) =>
        (await ControlAsync(HttpMethod.Get, "/api/modelconfigs", null, ct) as JsonArray ?? [])
        .Select(m => m?["ref"] is JsonValue r && r.TryGetValue<string>(out var s) ? s : null)
        .OfType<string>().ToList();

    /// <summary>
    /// A control call as this process (no person): the envelope's <c>data</c> (an empty object when there is none), or
    /// null for a 404 when <paramref name="notFoundIsNull"/>. Bodies may carry secrets (a tool server's companion Secret):
    /// kagent's answer is logged, the request never.
    /// </summary>
    private async Task<JsonNode?> ControlAsync(HttpMethod method, string endpoint, JsonObject? body, CancellationToken ct, bool notFoundIsNull = false)
    {
        using var request = new HttpRequestMessage(method, WithUser(endpoint, null));
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, _controlTimeout, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("kagent {Method} {Endpoint} answered {StatusCode}: {Content}", method, endpoint, (int)response.StatusCode, Cut(content));
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }
        try
        {
            return JsonNode.Parse(content) is JsonObject envelope ? envelope["data"]?.DeepClone() ?? new JsonObject() : new JsonObject();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "kagent {Method} {Endpoint} answered invalid JSON", method, endpoint);
            throw new HttpRequestException("kagent answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }
    }

    public async Task<List<ChatMessage>> GetSessionMessagesAsync(string sessionId, string? userId = null)
    {
        return await RequestAsync<List<ChatMessage>>($"/api/sessions/{Uri.EscapeDataString(sessionId)}/messages", HttpMethod.Get, null, userId);
    }

    public async Task<List<KAgentEvent>> GetSessionEventsAsync(string sessionId, int? limit = null, string? userId = null)
    {
        try
        {
            var url = $"/api/sessions/{Uri.EscapeDataString(sessionId)}";
            if (limit.HasValue)
            {
                url += $"?limit={limit}";
            }
            var response = await RequestAsync<SessionWithEventsResponse>(url, HttpMethod.Get, null, userId);
            return response?.Events ?? new List<KAgentEvent>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get session events for {SessionId}, returning empty list", sessionId);
            return new List<KAgentEvent>();
        }
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

    public async Task<List<Hook>> GetHooksAsync()
    {
        try
        {
            // khook API returns a HookList CRD (apiVersion, kind, metadata, items)
            var hookList = await RequestAsync<HookList>("/api/hooks", HttpMethod.Get);
            return hookList?.Items ?? new List<Hook>();
        }
        catch
        {
            // Fallback: try direct list deserialization
            return await RequestAsync<List<Hook>>("/api/hooks", HttpMethod.Get);
        }
    }

    public async Task<Hook> CreateHookAsync(Hook hook)
    {
        return await RequestAsync<Hook>("/api/hooks", HttpMethod.Post, hook);
    }

    public async Task<bool> EnableHookAsync(string hookId)
    {
        // Parse namespace/name from hookId
        var parts = hookId.Split('/');
        var ns = parts.Length > 1 ? parts[0] : "kagent";
        var name = parts.Length > 1 ? parts[1] : hookId;
        await RequestAsync<object>($"/api/hooks/{ns}/{name}/enable", HttpMethod.Post);
        return true;
    }

    public async Task<bool> DisableHookAsync(string hookId)
    {
        var parts = hookId.Split('/');
        var ns = parts.Length > 1 ? parts[0] : "kagent";
        var name = parts.Length > 1 ? parts[1] : hookId;
        await RequestAsync<object>($"/api/hooks/{ns}/{name}/disable", HttpMethod.Post);
        return true;
    }

    public async Task DeleteHookAsync(string ns, string name)
    {
        await RequestAsync<object>($"/api/hooks/{ns}/{name}", HttpMethod.Delete);
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
