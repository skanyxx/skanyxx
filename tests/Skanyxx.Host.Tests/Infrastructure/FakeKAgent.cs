using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// The slice of kagent 0.10.2's controller API the first hour uses, on real Kestrel (random loopback port), with the
/// shapes of the real handlers: <c>{error, data, message}</c> envelopes; agents with labels; one ModelConfig
/// (<c>kagent/default-model-config</c>) whose create/update put an inline <c>apiKey</c> into a "Secret" and point the spec
/// at it, as kagent does; per-user sessions (a session of another user is 404); and a blocking A2A <c>message/send</c>
/// that answers with a completed task. Every request is recorded with the user kagent would see. Knobs make it slow
/// (<see cref="AgentsDelay"/>, <see cref="ModelDelay"/>, <see cref="A2ADelay"/>), failing or odd-shaped.
/// Start it before <see cref="HostApp.StartAsync"/>: that one captures the next host built in the process.
/// </summary>
public sealed class FakeKAgent : IAsyncDisposable
{
    public const string ModelConfigPath = "/api/modelconfigs/kagent/default-model-config";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, (string User, string AgentId)> _sessions = new();

    private FakeKAgent(WebApplication app) => _app = app;

    public sealed record Call(string Method, string Path, string? QueryUser, string? HeaderUser, JsonNode? Body);

    public ConcurrentQueue<Call> Calls { get; } = new();

    /// <summary>Agents created through the API (<c>ns/name</c> → the CR as kagent stores it).</summary>
    public ConcurrentDictionary<string, JsonObject> AgentObjects { get; } = new();

    /// <summary>RemoteMCPServers created through the API (<c>ns/name</c>).</summary>
    public ConcurrentDictionary<string, JsonObject> ToolServers { get; } = new();

    /// <summary>Companion Secrets: name → (owning tool server, value). Deleted with their server, as Kubernetes GC does.</summary>
    public ConcurrentDictionary<string, (string Owner, string Value)> Secrets { get; } = new();

    /// <summary>How many tool server creates to refuse with a 500 (nothing created), as kagent down mid-apply.</summary>
    public int FailToolServerCreates;

    /// <summary>The tool server list repeats a ref and has an entry without one.</summary>
    public bool DuplicateToolServerRefs { get; set; }

    /// <summary>The stored ModelConfig spec (null = absent) and the API-key Secret kagent would hold.</summary>
    public JsonObject? ModelSpec { get; set; }

    public Dictionary<string, string> Secret { get; } = [];

    public bool ModelAccepted { get; set; } = true;

    public string Answer { get; set; } = "The refund window is 30 days [card company/refund-window].";

    public TimeSpan AgentsDelay { get; set; }

    public TimeSpan ModelDelay { get; set; }

    public TimeSpan A2ADelay { get; set; }

    /// <summary>Instead of the agent list: this status with a body naming kagent internals.</summary>
    public int? AgentsStatus { get; set; }

    /// <summary>Instead of the agent list: this raw body (200).</summary>
    public string? AgentsBody { get; set; }

    /// <summary>Instead of the completed task: this status with a body naming kagent internals.</summary>
    public int? A2AStatus { get; set; }

    /// <summary>Instead of the completed task: this raw body (200).</summary>
    public string? A2ABody { get; set; }

    public const string InternalDetail = "pod kagent-controller-7f/10.244.0.9 panicked: SECRET-INTERNAL";

    /// <summary>A create racing this one: the next POST finds this spec already there and answers 409, as kagent does.</summary>
    public JsonObject? CreatedMeanwhile { get; set; }

    public IReadOnlyCollection<string> SessionIds => [.. _sessions.Keys];

    public int Port => new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

    public IEnumerable<Call> CallsTo(string method, string pathPrefix) =>
        Calls.Where(c => c.Method == method && c.Path.StartsWith(pathPrefix, StringComparison.Ordinal));

    public static async Task<FakeKAgent> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var fake = new FakeKAgent(app);
        fake.Map(app);
        await app.StartAsync();
        return fake;
    }

    private void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            JsonNode? body = null;
            if (context.Request.ContentLength > 0)
            {
                body = await JsonNode.ParseAsync(context.Request.Body);
                context.Request.Body.Position = 0;
            }
            Calls.Enqueue(new Call(context.Request.Method, context.Request.Path, context.Request.Query["user_id"],
                context.Request.Headers["X-User-Id"], body));
            await next();
        });

        app.MapGet("/api/agents", async (HttpContext context) =>
        {
            await Task.Delay(AgentsDelay, context.RequestAborted);
            if (AgentsStatus is { } status)
                return Results.Json(new JsonObject { ["error"] = true, ["message"] = InternalDetail }, statusCode: status);
            if (AgentsBody is { } raw)
                return Results.Content(raw, "application/json");
            // Two merged agents share the name "seed" in different namespaces (the reconciler may merge into several).
            return Envelope(new JsonArray([.. FixedAgents().Select(a => (JsonNode)a),
                .. AgentObjects.Values.Select(a => (JsonNode)new JsonObject
                {
                    ["id"] = "dynamic", ["agent"] = a.DeepClone(), ["deploymentReady"] = true, ["accepted"] = true
                })]));
        });

        // The studio reconciler's calls (research/kagent-agent-api.md): GET one, POST keeps labels, PUT replaces the spec
        // only, DELETE; tool servers with companion Secrets; the ModelConfig list.
        app.MapGet("/api/agents/{ns}/{name}", (string ns, string name) =>
            AgentObjects.TryGetValue($"{ns}/{name}", out var agent)
                ? Envelope(new JsonObject { ["id"] = "dynamic", ["agent"] = agent.DeepClone() })
                : FixedAgents().FirstOrDefault(a => a["agent"]!["metadata"]!["name"]!.GetValue<string>() == name
                    && a["agent"]!["metadata"]!["namespace"]!.GetValue<string>() == ns) is { } fixedAgent
                    ? Envelope(fixedAgent)
                    : Results.Json(new JsonObject { ["error"] = "Agent not found" }, statusCode: 404));
        app.MapPost("/api/agents", (JsonObject agent) =>
        {
            var key = $"{agent["metadata"]!["namespace"]!.GetValue<string>()}/{agent["metadata"]!["name"]!.GetValue<string>()}";
            if (AgentObjects.ContainsKey(key) || FixedAgents().Any(a => a["agent"]!["metadata"]!["name"]!.GetValue<string>() == key.Split('/')[1]))
                return Results.Json(new JsonObject { ["error"] = "Failed to create Agent in Kubernetes" }, statusCode: 500);
            AgentObjects[key] = (JsonObject)agent.DeepClone();
            return Results.Json(new JsonObject { ["error"] = false, ["data"] = agent.DeepClone() }, statusCode: 201);
        });
        app.MapPut("/api/agents", (JsonObject agent) =>
        {
            var key = $"{agent["metadata"]!["namespace"]!.GetValue<string>()}/{agent["metadata"]!["name"]!.GetValue<string>()}";
            if (!AgentObjects.TryGetValue(key, out var existing))
                return Results.Json(new JsonObject { ["error"] = "Agent not found" }, statusCode: 404);
            existing["spec"] = agent["spec"]!.DeepClone();
            return Envelope(existing.DeepClone());
        });
        app.MapDelete("/api/agents/{ns}/{name}", (string ns, string name) =>
            AgentObjects.TryRemove($"{ns}/{name}", out _)
                ? Envelope(new JsonObject())
                : Results.Json(new JsonObject { ["error"] = "Agent not found" }, statusCode: 404));

        app.MapGet("/api/toolservers", () => Envelope(new JsonArray([
            new JsonObject { ["ref"] = "kagent/skanyxx-memory-seed", ["groupKind"] = "RemoteMCPServer.kagent.dev", ["discoveredTools"] = new JsonArray() },
            .. DuplicateToolServerRefs
                ? new JsonNode[] { new JsonObject { ["ref"] = "kagent/skanyxx-memory-seed" }, new JsonObject { ["groupKind"] = "RemoteMCPServer.kagent.dev" } }
                : [],
            .. ToolServers.Keys.Select(k => (JsonNode)new JsonObject
            {
                ["ref"] = k, ["groupKind"] = "RemoteMCPServer.kagent.dev",
                ["discoveredTools"] = new JsonArray(new JsonObject { ["name"] = "memory_search" }, new JsonObject { ["name"] = "memory_upsert" })
            })])));
        app.MapPost("/api/toolservers", (JsonObject body) =>
        {
            if (Interlocked.Decrement(ref FailToolServerCreates) >= 0)
                return Results.Json(new JsonObject { ["error"] = "kagent is restarting" }, statusCode: 500);
            var server = (JsonObject)body["remoteMCPServer"]!;
            var key = $"{server["metadata"]!["namespace"]!.GetValue<string>()}/{server["metadata"]!["name"]!.GetValue<string>()}";
            if (!ToolServers.TryAdd(key, (JsonObject)server.DeepClone()))
                return Results.Json(new JsonObject { ["error"] = "Failed to create RemoteMCPServer in Kubernetes" }, statusCode: 500);
            foreach (var secret in body["secrets"] as JsonArray ?? [])
                Secrets[secret!["name"]!.GetValue<string>()] = (key, secret["value"]!.GetValue<string>());
            return Results.Json(new JsonObject { ["error"] = false, ["data"] = server.DeepClone() }, statusCode: 201);
        });
        app.MapDelete("/api/toolservers/{ns}/{name}", (string ns, string name) =>
        {
            if (!ToolServers.TryRemove($"{ns}/{name}", out _))
                return Results.Json(new JsonObject { ["error"] = "ToolServer not found" }, statusCode: 404);
            foreach (var owned in Secrets.Where(s => s.Value.Owner == $"{ns}/{name}").Select(s => s.Key).ToList())
                Secrets.TryRemove(owned, out _);
            return Envelope(new JsonObject());
        });
        app.MapGet("/api/modelconfigs", () => Envelope(new JsonArray(
            new JsonObject { ["ref"] = "kagent/default-model-config" }, new JsonObject { ["ref"] = "other-ns/elsewhere" })));

        app.MapGet(ModelConfigPath, async (HttpContext context) =>
        {
            await Task.Delay(ModelDelay, context.RequestAborted);
            return ModelSpec is null
                ? Results.Json(new JsonObject { ["error"] = true, ["message"] = "ModelConfig not found" }, statusCode: 404)
                : Envelope(ModelConfigJson());
        });
        app.MapPost("/api/modelconfigs", (JsonObject body) =>
        {
            if (CreatedMeanwhile is not null)
            {
                ModelSpec = CreatedMeanwhile;
                CreatedMeanwhile = null;
            }
            if (ModelSpec is not null)
                return Results.Json(new JsonObject { ["error"] = true, ["message"] = "ModelConfig already exists" }, statusCode: 409);
            Store(body);
            return Results.Json(new JsonObject { ["error"] = false, ["data"] = ModelConfigJson() }, statusCode: 201);
        });
        app.MapPut(ModelConfigPath, (JsonObject body) =>
        {
            if (ModelSpec is null)
                return Results.Json(new JsonObject { ["error"] = true, ["message"] = "ModelConfig not found" }, statusCode: 404);
            Store(body);
            return Envelope(ModelConfigJson());
        });

        app.MapPost("/api/sessions", (HttpContext context, JsonObject body) =>
        {
            var id = Guid.NewGuid().ToString();
            var agentId = body["agent_ref"]!.GetValue<string>().Replace("-", "_").Replace("/", "__NS__");
            _sessions[id] = (UserOf(context), agentId);
            return Results.Json(new JsonObject { ["error"] = false, ["data"] = SessionJson(id) }, statusCode: 201);
        });
        app.MapGet("/api/sessions", (HttpContext context) =>
            Envelope(new JsonArray([.. _sessions.Where(s => s.Value.User == UserOf(context)).Select(s => (JsonNode)SessionJson(s.Key))])));
        app.MapGet("/api/sessions/{id}", (HttpContext context, string id) =>
            _sessions.TryGetValue(id, out var s) && s.User == UserOf(context)
                ? Envelope(new JsonObject { ["session"] = SessionJson(id), ["events"] = new JsonArray() })
                : Results.Json(new JsonObject { ["error"] = true, ["message"] = "Session not found" }, statusCode: 404));

        app.MapDelete("/api/sessions/{id}", (HttpContext context, string id) =>
            _sessions.TryGetValue(id, out var s) && s.User == UserOf(context) && _sessions.TryRemove(id, out _)
                ? Results.Json(new JsonObject { ["error"] = false, ["message"] = "Session deleted" })
                : Results.Json(new JsonObject { ["error"] = true, ["message"] = "Session not found" }, statusCode: 404));

        app.MapPost("/api/a2a/{ns}/{name}/", async (HttpContext context, JsonObject body) =>
        {
            await Task.Delay(A2ADelay, context.RequestAborted);
            if (A2AStatus is { } status)
                return Results.Json(new JsonObject { ["error"] = InternalDetail }, statusCode: status);
            if (A2ABody is { } raw)
                return Results.Content(raw, "application/json");
            return Results.Json(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = body["id"]!.DeepClone(),
                ["result"] = new JsonObject
                {
                    ["kind"] = "task",
                    ["contextId"] = body["params"]!["message"]!["contextId"]?.DeepClone(),
                    ["status"] = new JsonObject { ["state"] = "completed" },
                    ["artifacts"] = new JsonArray(new JsonObject
                    {
                        ["parts"] = new JsonArray(
                            new JsonObject { ["kind"] = "text", ["text"] = "thinking…", ["metadata"] = new JsonObject { ["adk_thought"] = true } },
                            new JsonObject { ["kind"] = "text", ["text"] = Answer })
                    })
                }
            });
        });
    }

    private static JsonObject[] FixedAgents() =>
    [
        AgentJson("seed", new JsonObject { ["app.kubernetes.io/part-of"] = "skanyxx-memory", ["skanyxx.dev/merged"] = "true" }),
        AgentJson("seed", new JsonObject { ["skanyxx.dev/merged"] = "true" }, "team-a"),
        AgentJson("ticket-planner", new JsonObject { ["app.kubernetes.io/part-of"] = "skanyxx-ticket-flow" }),
        AgentJson("e2e-hello", null)
    ];

    // kagent unsecure mode: ?user_id=, else X-User-Id, else admin@kagent.dev.
    private static string UserOf(HttpContext context) =>
        context.Request.Query["user_id"].FirstOrDefault() ?? context.Request.Headers["X-User-Id"].FirstOrDefault() ?? "admin@kagent.dev";

    private JsonObject SessionJson(string id) => new()
    {
        ["id"] = id, ["user_id"] = _sessions[id].User, ["agent_id"] = _sessions[id].AgentId
    };

    // As kagent's handler: an inline key with no secret named → Secret named like the ModelConfig, <PROVIDER>_API_KEY,
    // merged into the existing Secret (StringData); after the spec flip, an owned Secret the spec stops naming is deleted.
    private void Store(JsonObject body)
    {
        var oldSecret = ModelSpec?["apiKeySecret"]?.GetValue<string>();
        var spec = (JsonObject)body["spec"]!.DeepClone();
        var provider = spec["provider"]?.GetValue<string>() ?? "OpenAI";
        if (body["apiKey"]?.GetValue<string>() is { Length: > 0 } key && spec["apiKeySecret"] is null && provider != "Ollama")
        {
            spec["apiKeySecret"] = "default-model-config";
            spec["apiKeySecretKey"] = $"{provider.ToUpperInvariant()}_API_KEY";
            Secret[spec["apiKeySecretKey"]!.GetValue<string>()] = key;
        }
        ModelSpec = spec;
        if (oldSecret is not null && spec["apiKeySecret"] is null)
            Secret.Clear();
    }

    private JsonObject ModelConfigJson() => new()
    {
        ["ref"] = "kagent/default-model-config",
        ["spec"] = ModelSpec!.DeepClone(),
        ["status"] = new JsonObject
        {
            ["conditions"] = new JsonArray(new JsonObject
            {
                ["type"] = "Accepted", ["status"] = ModelAccepted ? "True" : "False", ["message"] = ModelAccepted ? "Model configuration accepted" : "secret not found"
            })
        }
    };

    private static JsonObject AgentJson(string name, JsonObject? labels, string ns = "kagent") => new()
    {
        ["id"] = $"{ns.Replace('-', '_')}__NS__{name.Replace('-', '_')}",
        ["agent"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = ns, ["labels"] = labels },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["description"] = $"{name} agent" }
        },
        ["deploymentReady"] = true,
        ["accepted"] = true
    };

    private static IResult Envelope(JsonNode data) => Results.Json(new JsonObject { ["error"] = false, ["data"] = data, ["message"] = "ok" });

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
