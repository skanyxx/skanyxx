using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Core.Platform.Chat;

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

    /// <summary>The default model, <c>namespace/name</c> (<c>KAgent:ModelConfig</c>): the one the seed agents and Chat use.</summary>
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
            else if (IsEmptyList(root))
                return [];
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
        var type = GetStringProperty(spec, "type") ?? "Declarative";
        var ns = GetStringProperty(metadata, "namespace") ?? "kagent";

        var isReady = item.TryGetProperty("deploymentReady", out var readyProp) && readyProp.ValueKind == JsonValueKind.True
            || HasCondition(status, "Ready");
        var isAccepted = ConditionStatus(status, "Accepted");

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
            Type = type,
            Description = GetStringProperty(spec, "description") ?? "",
            Status = isReady ? "Active" : "Inactive",
            Ready = isReady,
            Accepted = isAccepted,
            Labels = labels,
            Suspended = Replicas(spec) == 0,
            ModelConfig = type == "Declarative" ? DeclarativeModelConfig(spec) : null,
            McpToolNames = McpToolNames(spec),
            McpServerRefs = McpServerRefs(spec, ns),
            AgentToolNames = AgentToolNames(spec),
            AgentToolRefs = AgentToolRefs(spec, ns)
        };
    }

    // kagent: "If not specified, the default value is default-model-config", in the agent's namespace.
    private static string DeclarativeModelConfig(JsonElement spec) =>
        spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("declarative", out var declarative)
            && GetStringProperty(declarative, "modelConfig") is { Length: > 0 } model
            ? model
            : "default-model-config";

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

    private static List<string> McpServerRefs(JsonElement spec, string agentNamespace) =>
        spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("declarative", out var declarative) && declarative.ValueKind == JsonValueKind.Object
        && declarative.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.Object && t.TryGetProperty("mcpServer", out var s) && s.ValueKind == JsonValueKind.Object)
                .Select(t => t.GetProperty("mcpServer"))
                .Select(s => GetStringProperty(s, "name") is { } name ? $"{NamespaceOr(s, agentNamespace)}/{name}" : null)
                .OfType<string>().Distinct()]
            : [];

    private static List<string> AgentToolNames(JsonElement spec) =>
        spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("declarative", out var declarative) && declarative.ValueKind == JsonValueKind.Object
        && declarative.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object && t.TryGetProperty("agent", out _))
                .Select(t => GetStringProperty(t.GetProperty("agent"), "name")).OfType<string>()]
            : [];

    private static List<string> AgentToolRefs(JsonElement spec, string agentNamespace) =>
        spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("declarative", out var declarative) && declarative.ValueKind == JsonValueKind.Object
        && declarative.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.Object && t.TryGetProperty("agent", out var a) && a.ValueKind == JsonValueKind.Object)
                .Select(t => t.GetProperty("agent"))
                // An entry without a name stays as a ref kagent never lists, so ChatOffer excludes its caller (fails closed).
                .Select(a => $"{NamespaceOr(a, agentNamespace)}/{GetStringProperty(a, "name") ?? ""}")
                .Distinct()]
            : [];

    // An empty namespace is kagent's default, the agent's own (as ToolServerHealth.ServerRefs reads it).
    private static string NamespaceOr(JsonElement reference, string agentNamespace) =>
        GetStringProperty(reference, "namespace") is { Length: > 0 } ns ? ns : agentNamespace;

    private static bool HasCondition(JsonElement status, string type) =>
        status.ValueKind == JsonValueKind.Object
        && status.TryGetProperty("conditions", out var conditions)
        && conditions.ValueKind == JsonValueKind.Array
        && conditions.EnumerateArray().Any(c => GetStringProperty(c, "type") == type && GetStringProperty(c, "status") == "True");

    /// <summary>The condition's status: true or false as kagent set it, null while it has none of that type.</summary>
    private static bool? ConditionStatus(JsonElement status, string type)
    {
        if (status.ValueKind != JsonValueKind.Object || !status.TryGetProperty("conditions", out var conditions) || conditions.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var c in conditions.EnumerateArray())
            if (GetStringProperty(c, "type") == type)
                return GetStringProperty(c, "status") switch { "True" => true, "False" => false, _ => null };
        return null;
    }

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
    /// <paramref name="messageId"/> names the message, so <see cref="CancelBlockingTurnAsync"/> can find its task.
    /// </summary>
    public async Task<ChatResponse> SendMessageAsync(
        string agentNamespace, string agentName, string sessionId, string message, string userId, CancellationToken ct = default,
        IReadOnlyList<A2AFilePart>? files = null, string? messageId = null)
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
                        messageId = messageId ?? Guid.NewGuid().ToString(),
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
            answer = Answer(body, $"{agentNamespace}/{agentName}");
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

    private string Answer(JsonNode? body, string agentRef)
    {
        if (body?["error"] is { } error)
        {
            _logger.LogWarning("kagent JSON-RPC error: {Error}", Cut(SecretScan.RedactJson(error)));
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
                // Cleaned once, for this line and for the caller (D553): no control character forges a log line.
                var taskId = ChatFailureReason.Of(result["id"]?.GetValue<string>());
                // An approval stop is no failure: its text (a sign-in link with its state) is neither logged nor passed on.
                var approval = state is "input-required" or "auth-required";
                var reason = approval ? null : ChatFailureReason.Of(Text(result["status"]?["message"]?["parts"]));
                if (approval)
                    _logger.LogInformation("kagent task {TaskId} of {AgentRef} stopped for an approval ({State})", taskId, agentRef, state);
                else
                    _logger.LogWarning("kagent task {TaskId} of {AgentRef} ended {State}: {Detail}",
                        taskId, agentRef, ChatFailureReason.Of(state) ?? "?", reason ?? "(no message)");
                throw new KAgentTaskFailedException(taskId, ChatFailureReason.Of(state) ?? "?", reason, A2AUsage.From(result));
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

    /// <summary>
    /// kagent's text for a log line, with anything that looks like a credential blanked (D391): an error that echoes a
    /// spec or a turn could carry a header value or a key. Redacted first, so a credential the cut would halve is still
    /// blanked whole.
    /// </summary>
    private static string Cut(string text) => SecretScan.Redact(text) is var redacted && redacted.Length > 1_000 ? redacted[..1_000] : redacted;

    public const string SecretCallRefused = "its answer is not logged: the request carried a secret value";

    private void LogSecretCallRefused(HttpResponseMessage response) =>
        _logger.LogWarning("kagent {Method} {Endpoint} answered {StatusCode}; " + SecretCallRefused,
            response.RequestMessage?.Method, response.RequestMessage?.RequestUri?.AbsolutePath, (int)response.StatusCode);

    /// <summary>Every ModelConfig kagent has, as kagent returns them: <c>[{ref, spec, status}]</c>.</summary>
    public Task<JsonArray> GetModelConfigsAsync(CancellationToken ct) => ControlListAsync("/api/modelconfigs", ct);

    /// <summary>The ModelConfig <c>{ref, spec, status}</c>, or null when kagent has none by that name.</summary>
    public async Task<JsonObject?> GetModelConfigAsync(string ns, string name, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Get, ModelConfigPath(ns, name), null, ct, notFoundIsNull: true) switch
        {
            null => null,
            JsonObject config => config,
            _ => throw UnexpectedShape()
        };

    /// <summary>
    /// Creates the ModelConfig (kagent answers 409 when the name is taken). A non-empty <paramref name="apiKey"/> goes
    /// inline: kagent writes it into a Secret named like the ModelConfig and points the spec at it (only when the spec
    /// names no secret). The request is never logged, so the key never is.
    /// </summary>
    public async Task<JsonObject> CreateModelConfigAsync(string ns, string name, JsonObject spec, string? apiKey, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Post, "/api/modelconfigs", ModelConfigBody($"{ns}/{name}", spec, apiKey), ct,
            carriesSecret: !string.IsNullOrEmpty(apiKey)) as JsonObject
        ?? throw UnexpectedShape();

    /// <summary>Replaces the ModelConfig's spec (kagent keeps no other field); the key as in <see cref="CreateModelConfigAsync"/>.</summary>
    public async Task<JsonObject> UpdateModelConfigAsync(string ns, string name, JsonObject spec, string? apiKey, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Put, ModelConfigPath(ns, name), ModelConfigBody(null, spec, apiKey), ct,
            carriesSecret: !string.IsNullOrEmpty(apiKey)) as JsonObject
        ?? throw UnexpectedShape();

    /// <returns>False when kagent had no such ModelConfig. Its owned key Secret goes with it (Kubernetes GC).</returns>
    public async Task<bool> DeleteModelConfigAsync(string ns, string name, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Delete, ModelConfigPath(ns, name), null, ct, notFoundIsNull: true) is not null;

    /// <summary>The providers kagent supports: <c>[{name, type, requiredParams, optionalParams}]</c>.</summary>
    public async Task<JsonArray> GetModelProvidersAsync(CancellationToken ct) =>
        await ControlListAsync("/api/modelproviderconfigs/models", ct);

    /// <summary>kagent's supported models per provider: <c>{"OpenAI": [{name, function_calling}], …}</c>.</summary>
    public async Task<JsonObject> GetSupportedModelsAsync(CancellationToken ct) =>
        await ControlAsync(HttpMethod.Get, "/api/models", null, ct) as JsonObject ?? throw UnexpectedShape();

    private static string ModelConfigPath(string ns, string name) =>
        $"/api/modelconfigs/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}";

    private static JsonObject ModelConfigBody(string? configRef, JsonObject spec, string? apiKey)
    {
        var body = new JsonObject { ["spec"] = spec.DeepClone() };
        if (configRef is not null)
            body["ref"] = configRef;
        if (!string.IsNullOrEmpty(apiKey))
            body["apiKey"] = apiKey;
        return body;
    }

    private static HttpRequestException UnexpectedShape() => new("kagent answered in an unexpected shape", null, HttpStatusCode.BadGateway);

    /// <summary>The Agent CR <c>{apiVersion, kind, metadata, spec, status}</c>, or null when kagent has none by that name.</summary>
    public async Task<JsonObject?> GetAgentObjectAsync(string ns, string name, CancellationToken ct)
    {
        var data = await ControlAsync(HttpMethod.Get, $"/api/agents/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}", null, ct, notFoundIsNull: true);
        return data is null ? null : data["agent"] as JsonObject
            ?? throw new HttpRequestException("kagent answered in an unexpected shape", null, HttpStatusCode.BadGateway);
    }

    /// <summary>
    /// Creates the Agent CR as given, labels included (kagent keeps them; an update never changes them). Secret-carrying
    /// like <see cref="UpdateAgentAsync"/>: an imported manifest may hold inline values too.
    /// </summary>
    public Task CreateAgentAsync(JsonObject agent, CancellationToken ct) => ControlAsync(HttpMethod.Post, "/api/agents", agent, ct, carriesSecret: true);

    /// <summary>
    /// Replaces the Agent's spec; kagent ignores the body's metadata except its name and namespace. An edit, suspend or
    /// resume sends back the whole existing Agent, and one made outside Skanyxx may hold an inline <c>value</c> under a
    /// tool's <c>headersFrom</c> or the deployment's <c>env</c>, so a refusal never logs kagent's answer (D327).
    /// </summary>
    public Task UpdateAgentAsync(JsonObject agent, CancellationToken ct) => ControlAsync(HttpMethod.Put, "/api/agents", agent, ct, carriesSecret: true);

    /// <returns>False when kagent had no such agent.</returns>
    public async Task<bool> DeleteAgentAsync(string ns, string name, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Delete, $"/api/agents/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}", null, ct, notFoundIsNull: true) is not null;

    /// <summary>kagent's tool servers: <c>[{ref: "ns/name", groupKind, discoveredTools: [{name, description}]}]</c>.</summary>
    public Task<JsonArray> GetToolServerListAsync(CancellationToken ct) => ControlListAsync("/api/toolservers", ct);

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
        (await GetModelConfigsAsync(ct))
        .Select(m => m?["ref"] is JsonValue r && r.TryGetValue<string>(out var s) ? s : null)
        .OfType<string>().ToList();

    /// <summary>
    /// A control call as this process (no person): the envelope's <c>data</c> (an empty object when there is none; an
    /// empty array for a <paramref name="list"/> kagent answered without <c>data</c>, D540), or
    /// null for a 404 when <paramref name="notFoundIsNull"/>. The request is never logged; kagent's refusal is, up to 1 000
    /// characters, unless the request <paramref name="carriesSecret"/> (a tool server's companion Secret values, a
    /// ModelConfig write with an inline API key, any Agent write): kagent's answer could echo them, so then only the method, path, status
    /// and a fixed text are logged. The exception never carries kagent's text.
    /// </summary>
    private async Task<JsonNode?> ControlAsync(HttpMethod method, string endpoint, JsonObject? body, CancellationToken ct,
        bool notFoundIsNull = false, bool carriesSecret = false, bool list = false)
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
            var envelope = JsonNode.Parse(content) as JsonObject;
            if (list && envelope is not null && !envelope.ContainsKey("data") && envelope["error"]?.GetValueKind() == JsonValueKind.False)
                return new JsonArray();
            return envelope?["data"]?.DeepClone() ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "kagent {Method} {Endpoint} answered invalid JSON", method, endpoint);
            throw new HttpRequestException("kagent answered in an unexpected shape", ex, HttpStatusCode.BadGateway);
        }
    }

    /// <summary>kagent's agent list entries as they come (<c>{agent, model, modelProvider, modelConfigRef, deploymentReady}</c>).</summary>
    public Task<JsonArray> GetAgentListAsync(CancellationToken ct) => ControlListAsync("/api/agents", ct);

    /// <summary>
    /// A list call: kagent 0.10.2 leaves <c>data</c> out of the envelope when the list is empty (a fresh install has no
    /// agents, ModelConfigs or tool servers).
    /// </summary>
    private async Task<JsonArray> ControlListAsync(string endpoint, CancellationToken ct) =>
        await ControlAsync(HttpMethod.Get, endpoint, null, ct, list: true) as JsonArray ?? throw UnexpectedShape();

    /// <summary><c>{"error": false, "message": …}</c> without <c>data</c>: kagent's answer for an empty list.</summary>
    private static bool IsEmptyList(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("data", out _)
        && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.False;

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

    /// <summary>
    /// Asks kagent to stop a running task (the Stop button, a turn the caller left); kagent may already have finished it.
    /// A refusal (an HTTP error or a JSON-RPC error) is logged, without kagent's words, and not thrown: there is nothing
    /// more to do about a turn nobody waits for.
    /// </summary>
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
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("kagent did not cancel task {TaskId} of {Namespace}/{Agent}: HTTP {StatusCode}", taskId, agentNamespace, agentName, (int)response.StatusCode);
            return;
        }
        JsonNode? body = null;
        try
        {
            body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException)
        {
            // Logged below as an answer of another shape.
        }
        if (body is not JsonObject rpc || rpc["error"] is not null)
            _logger.LogWarning("kagent did not cancel task {TaskId} of {Namespace}/{Agent}: {Reason}", taskId, agentNamespace, agentName,
                body is JsonObject ? "a JSON-RPC error" : "an answer of another shape");
    }

    /// <summary>
    /// Stops the blocking turn that sent <paramref name="messageId"/> in <paramref name="sessionId"/>, when the caller left
    /// it or our deadline passed (D381, D388): only that turn's task, never another turn running in the same session. Best
    /// effort, so never throws; a failure is logged.
    /// </summary>
    public async Task CancelBlockingTurnAsync(string agentNamespace, string agentName, string sessionId, string messageId, string userId)
    {
        try
        {
            foreach (var task in await RunningTaskIdsAsync(sessionId, userId, messageId, CancellationToken.None))
                await CancelTaskAsync(agentNamespace, agentName, task, userId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            _logger.LogWarning("kagent did not cancel the turn in {SessionId} ({Error}); it may have finished", sessionId, ex.GetType().Name);
        }
    }

    /// <summary>
    /// The ids of <paramref name="userId"/>'s tasks in <paramref name="sessionId"/> that have not ended and that
    /// <paramref name="messageId"/> started (kagent's <c>GET /api/sessions/{id}/tasks</c>: A2A tasks with
    /// <c>status.state</c> and <c>history</c>). Asked on the 0.3 wire explicitly, and the v1 <c>TASK_STATE_*</c> names
    /// are read too. When kagent keeps no history, the one running task, if there is exactly one; otherwise none.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunningTaskIdsAsync(string sessionId, string userId, string messageId, CancellationToken ct)
    {
        var running = (await AsUserAsync(HttpMethod.Get, $"/api/sessions/{Uri.EscapeDataString(sessionId)}/tasks", null, userId, ct, a2aWire: true) as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(t => t["status"]?["state"] is JsonValue state && state.TryGetValue<string>(out var s) && !Ended(s))
            .ToList();
        var mine = running.Where(t => t["history"] is JsonArray history
            && history.OfType<JsonObject>().Any(m => m["messageId"] is JsonValue id && id.TryGetValue<string>(out var v) && v == messageId)).ToList();
        if (mine.Count == 0 && running is [{ } only] && only["history"] is not JsonArray)
            mine = running;
        return [.. mine.Select(t => t["id"] is JsonValue id && id.TryGetValue<string>(out var v) ? v : null).OfType<string>()];
    }

    private static bool Ended(string state) =>
        state.Replace("TASK_STATE_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant() is "completed" or "failed" or "rejected" or "canceled" or "cancelled";

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
            _logger.LogWarning("kagent JSON-RPC error: {Error}", Cut(SecretScan.RedactJson(error)));
            throw new HttpRequestException("kagent refused the message");
        }
        return body?["result"] ?? throw new HttpRequestException("kagent answered without a result", null, HttpStatusCode.BadGateway);
    }

    /// <summary>A call as <paramref name="userId"/>: the envelope's <c>data</c> (null when there is none).</summary>
    private async Task<JsonNode?> AsUserAsync(HttpMethod method, string endpoint, JsonObject? body, string userId, CancellationToken ct, bool a2aWire = false)
    {
        using var request = new HttpRequestMessage(method, WithUser(endpoint, userId));
        request.Headers.Add("X-User-ID", userId);
        // kagent's default for a missing header is the 0.3 wire, marked "revisit" upstream: never rely on it.
        if (a2aWire)
            request.Headers.Add("A2A-Version", "0.3");
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
