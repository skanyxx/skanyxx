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

/// <summary>Argo CD's <c>GET /api/v1/applications/{name}</c> (D363): one Application whose status a test sets; needs <see cref="Token"/>.</summary>
public sealed class FakeArgo : IAsyncDisposable
{
    public const string Token = "argo-token-NEVER-SHOWN-9876543210";

    private readonly WebApplication _app;

    private FakeArgo(WebApplication app) => _app = app;

    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>Application name → its JSON (<c>status.sync</c>, <c>status.health</c>, <c>status.resources</c>).</summary>
    public ConcurrentDictionary<string, JsonObject> Applications { get; } = new();

    public string BaseUrl => $"http://127.0.0.1:{new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port}";

    public static async Task<FakeArgo> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var fake = new FakeArgo(app);
        app.MapGet("/api/v1/applications/{name}", (string name, HttpRequest request) =>
        {
            fake.Calls.Enqueue(name);
            if (request.Headers.Authorization.ToString() != $"Bearer {Token}")
                return Results.Json(new { error = "no session information", code = 16 }, statusCode: 401);
            return fake.Applications.TryGetValue(name, out var application) ? Results.Json(application) : Results.Json(new { code = 5 }, statusCode: 404);
        });
        await app.StartAsync();
        return fake;
    }

    /// <summary>An Application whose status lists one Agent resource with the given sync and health status.</summary>
    public static JsonObject Application(string agent, string sync, string health, string? phase = "Succeeded") => new()
    {
        ["metadata"] = new JsonObject { ["name"] = "skanyxx-agents" },
        ["status"] = new JsonObject
        {
            ["sync"] = new JsonObject { ["status"] = sync },
            ["health"] = new JsonObject { ["status"] = health },
            ["operationState"] = phase is null ? null : new JsonObject { ["phase"] = phase },
            ["resources"] = new JsonArray(new JsonObject
            {
                ["kind"] = "Agent", ["name"] = agent, ["namespace"] = "kagent", ["status"] = sync, ["health"] = new JsonObject { ["status"] = health }
            })
        }
    };

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
