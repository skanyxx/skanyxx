using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Analytics;

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
    // process's own anonymous user (agent and tool server lists); Chat passes the signed-in person (D5).
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
            Revision = metadata.TryGetProperty("generation", out var generation) && generation.ValueKind == JsonValueKind.Number ? generation.GetRawText() : "",
            Type = GetStringProperty(spec, "type") ?? "Declarative",
            Description = GetStringProperty(spec, "description") ?? "",
            Status = isReady ? "Active" : "Inactive",
            Ready = isReady,
            Accepted = isAccepted,
            Labels = labels,
            Suspended = Replicas(spec) == 0,
            McpToolNames = McpToolNames(spec),
            ModelConfig = spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("declarative", out var d) ? GetStringProperty(d, "modelConfig") ?? "" : "",
            AgentToolNames = AgentToolNames(spec)
        };
    }

    private static int? Replicas(JsonElement spec)
    {
        foreach (var holder in new[] { "declarative", "byo" })
            if (spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty(holder, out var h) && h.ValueKind == JsonValueKind.Object
                && h.TryGetProperty("deployment", out var d) && d.ValueKind == JsonValueKind.Object
                && d.TryGetProperty("replicas", out var r) && r.ValueKind == JsonValueKind.Number)
                return r.GetInt32();
        return null;
    }

    private static Dictionary<string, List<string>> McpToolNames(JsonElement spec)
    {
        var tools = new Dictionary<string, List<string>>();
        if (spec.ValueKind != JsonValueKind.Object || !spec.TryGetProperty("declarative", out var declarative) || declarative.ValueKind != JsonValueKind.Object
            || !declarative.TryGetProperty("tools", out var list) || list.ValueKind != JsonValueKind.Array)
            return tools;
        foreach (var tool in list.EnumerateArray())
            if (tool.ValueKind == JsonValueKind.Object && tool.TryGetProperty("mcpServer", out var server) && GetStringProperty(server, "name") is { } name)
                tools[name] = server.TryGetProperty("toolNames", out var names) && names.ValueKind == JsonValueKind.Array
                    ? [.. names.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()!)]
                    : [];
        return tools;
    }

    private static List<string> AgentToolNames(JsonElement spec) =>
        spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("declarative", out var declarative) && declarative.ValueKind == JsonValueKind.Object
        && declarative.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object && t.TryGetProperty("agent", out _))
                .Select(t => GetStringProperty(t.GetProperty("agent"), "name")).OfType<string>()]
            : [];

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

    /// <summary>kagent's controller answers its health route within the control deadline; otherwise this throws.</summary>
    public async Task PingAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        using var response = await SendAsync(request, _controlTimeout, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
    }

    /// <param name="name">The session's display name (Chat: the first line of the first message).</param>
    public async Task<KAgentSession> CreateSessionAsync(string agentName, string? userId = null, CancellationToken ct = default, string? name = null)
    {
        // KAgent expects agent_ref in format "namespace/agent-name"
        var agentRef = agentName.Contains("/") ? agentName : $"kagent/{agentName}";
        return await RequestAsync<KAgentSession>("/api/sessions", HttpMethod.Post, new
        {
            user_id = userId ?? _userId,
            agent_ref = agentRef,
            name
        }, userId, ct);
    }

    /// <summary><paramref name="userId"/>'s session <paramref name="sessionId"/>; null when kagent does not know it.</summary>
    public async Task<KAgentSession?> GetSessionAsync(string sessionId, string? userId = null, CancellationToken ct = default) =>
        (await GetSessionWithEventsAsync(sessionId, userId, ct))?.Session;

    /// <summary>The session and its events, as <paramref name="userId"/>; null when kagent does not know it.</summary>
    public async Task<KAgentSessionEnvelope?> GetSessionWithEventsAsync(string sessionId, string? userId = null, CancellationToken ct = default)
    {
        try
        {
            return await RequestAsync<KAgentSessionEnvelope>($"/api/sessions/{Uri.EscapeDataString(sessionId)}", HttpMethod.Get, null, userId, ct);
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
        string agentNamespace, string agentName, string sessionId, string message, string userId, CancellationToken ct = default,
        IReadOnlyList<A2AFilePart>? files = null)
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
                        parts = MessageParts(message, files ?? []),
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
        TokenUsage usage;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            answer = Answer(body);
            usage = A2AUsage.From((body as JsonObject)?["result"]);
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
            Message = new ChatMessage { Content = answer, Role = "assistant", SessionId = sessionId, Timestamp = DateTime.UtcNow },
            Usage = usage
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

    public const string SecretCallRefused = "its answer is not logged: the request carried a secret value";

    private void LogSecretCallRefused(HttpResponseMessage response) =>
        _logger.LogWarning("kagent {Method} {Endpoint} answered {StatusCode}; " + SecretCallRefused,
            response.RequestMessage?.Method, response.RequestMessage?.RequestUri?.AbsolutePath, (int)response.StatusCode);

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
        return await ModelConfigDataAsync(response, ct, carriesSecret: body.ContainsKey("apiKey"));
    }

    private async Task<JsonObject> ModelConfigDataAsync(HttpResponseMessage response, CancellationToken ct, bool carriesSecret = false)
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
            // A write that carried the API key gets a fixed text: kagent's message could echo what it was sent.
            if (carriesSecret)
            {
                LogSecretCallRefused(response);
                throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }
            // Otherwise kagent's message names what failed. Owner-only path, so the text is shown to the owner.
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
        }, ct, carriesSecret: secrets.Count > 0);

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
    /// null for a 404 when <paramref name="notFoundIsNull"/>. The request is never logged; kagent's refusal is, up to 1 000
    /// characters, unless the request <paramref name="carriesSecret"/> (a tool server's companion Secret values): kagent's
    /// answer could echo them, so then only the status and a fixed text are logged.
    /// </summary>
    private async Task<JsonNode?> ControlAsync(HttpMethod method, string endpoint, JsonObject? body, CancellationToken ct,
        bool notFoundIsNull = false, bool carriesSecret = false)
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
            if (carriesSecret)
                LogSecretCallRefused(response);
            else
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

    /// <summary>kagent's agent list entries as they come (<c>{agent, model, modelProvider, modelConfigRef, deploymentReady}</c>).</summary>
    public async Task<JsonArray> GetAgentListAsync(CancellationToken ct) =>
        await ControlAsync(HttpMethod.Get, "/api/agents", null, ct) as JsonArray
        ?? throw new HttpRequestException("kagent answered in an unexpected shape", null, HttpStatusCode.BadGateway);

    /// <summary><paramref name="userId"/>'s sessions, as kagent lists them for that person (theirs only).</summary>
    public async Task<List<KAgentSession>> ListSessionsAsync(string userId, CancellationToken ct) =>
        (await AsUserAsync(HttpMethod.Get, "/api/sessions", null, userId, ct))?.Deserialize<List<KAgentSession>>() ?? [];

    /// <summary>Sets the display name of <paramref name="userId"/>'s session; false when kagent has no such session for them.</summary>
    public async Task<bool> RenameSessionAsync(string sessionId, string userId, string name, CancellationToken ct)
    {
        try
        {
            await AsUserAsync(HttpMethod.Patch, $"/api/sessions/{Uri.EscapeDataString(sessionId)}", new JsonObject { ["name"] = name }, userId, ct);
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// One streaming A2A <c>message/stream</c> in the session <paramref name="sessionId"/>, as <paramref name="userId"/>:
    /// each server-sent event's JSON-RPC <c>result</c> (a task, a status update, an artifact update or a message), in
    /// order. The whole stream is bounded by <see cref="KAgentConfig.ChatTimeoutSeconds"/>; cancelling
    /// <paramref name="ct"/> closes it (the agent's run is cut with the connection).
    /// </summary>
    public async IAsyncEnumerable<JsonNode> StreamMessageAsync(
        string agentNamespace, string agentName, string sessionId, string message, IReadOnlyList<A2AFilePart> files, string userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_chatTimeout);
        var parts = MessageParts(message, files);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            WithUser($"/api/a2a/{Uri.EscapeDataString(agentNamespace)}/{Uri.EscapeDataString(agentName)}/", userId))
        {
            Content = new StringContent(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Guid.NewGuid().ToString(),
                ["method"] = "message/stream",
                ["params"] = new JsonObject
                {
                    ["message"] = new JsonObject
                    {
                        ["kind"] = "message", ["messageId"] = Guid.NewGuid().ToString(), ["role"] = "user", ["parts"] = parts, ["contextId"] = sessionId
                    }
                }
            }.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("A2A-Version", "0.3");
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await WithinChatDeadline(() => _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token), ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("kagent A2A stream {Namespace}/{Agent} answered {StatusCode}: {Content}",
                agentNamespace, agentName, (int)response.StatusCode, Cut(await response.Content.ReadAsStringAsync(ct)));
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }

        using var reader = new StreamReader(await WithinChatDeadline(() => response.Content.ReadAsStreamAsync(deadline.Token), ct));
        var data = new StringBuilder();
        while (true)
        {
            var line = await WithinChatDeadline(() => reader.ReadLineAsync(deadline.Token).AsTask(), ct);
            if (line is not null && line.StartsWith("data:", StringComparison.Ordinal))
                data.Append(line.AsSpan(5).TrimStart());
            else if ((line is null || line.Length == 0) && data.Length > 0)
            {
                yield return StreamResult(data.ToString(), agentNamespace, agentName);
                data.Clear();
            }
            if (line is null)
                yield break;
        }
    }

    /// <summary>Asks kagent to stop a running task (the Stop button); kagent may already have finished it.</summary>
    public async Task CancelTaskAsync(string agentNamespace, string agentName, string taskId, string userId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            WithUser($"/api/a2a/{Uri.EscapeDataString(agentNamespace)}/{Uri.EscapeDataString(agentName)}/", userId))
        {
            Content = new StringContent(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = Guid.NewGuid().ToString(), ["method"] = "tasks/cancel", ["params"] = new JsonObject { ["id"] = taskId }
            }.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("A2A-Version", "0.3");
        request.Headers.Add("X-User-Id", userId);
        using var response = await SendAsync(request, _controlTimeout, ct);
    }

    // A2A message parts: the text, then each file inline (base64), as kagent hands them to the model.
    private static JsonArray MessageParts(string message, IReadOnlyList<A2AFilePart> files) =>
    [
        new JsonObject { ["kind"] = "text", ["text"] = message },
        .. files.Select(f => (JsonNode)new JsonObject
        {
            ["kind"] = "file",
            ["file"] = new JsonObject { ["name"] = f.Name, ["mimeType"] = f.MimeType, ["bytes"] = Convert.ToBase64String(f.Bytes) }
        })
    ];

    private async Task<T> WithinChatDeadline<T>(Func<Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"kagent did not finish the answer within {_chatTimeout.TotalSeconds:0} s.");
        }
    }

    private JsonNode StreamResult(string data, string agentNamespace, string agentName)
    {
        JsonNode? body;
        try
        {
            body = JsonNode.Parse(data);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "kagent A2A stream {Namespace}/{Agent} sent invalid JSON", agentNamespace, agentName);
            throw new HttpRequestException("kagent answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }
        if (body?["error"] is { } error)
        {
            _logger.LogWarning("kagent JSON-RPC error: {Error}", Cut(error.ToJsonString()));
            throw new HttpRequestException("kagent refused the message");
        }
        return body?["result"] ?? throw new HttpRequestException("kagent answered without a result", null, HttpStatusCode.BadGateway);
    }

    /// <summary>A call as <paramref name="userId"/>: the envelope's <c>data</c> (null when there is none).</summary>
    private async Task<JsonNode?> AsUserAsync(HttpMethod method, string endpoint, JsonObject? body, string userId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, WithUser(endpoint, userId));
        request.Headers.Add("X-User-ID", userId);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, _controlTimeout, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("kagent {Method} {Endpoint} answered {StatusCode}: {Content}", method, endpoint, (int)response.StatusCode, Cut(content));
            throw new HttpRequestException($"kagent answered HTTP {(int)response.StatusCode}", null, response.StatusCode);
        }
        try
        {
            return JsonNode.Parse(content)?["data"]?.DeepClone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "kagent {Method} {Endpoint} answered invalid JSON", method, endpoint);
            throw new HttpRequestException("kagent answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }
    }
}
