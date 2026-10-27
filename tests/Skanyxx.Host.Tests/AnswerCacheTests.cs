using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D241 end to end: the Host with the answer cache on, against a real Redis (Dragonfly speaks the same protocol). The
/// module loads StackExchange.Redis from the Host (PluginLoadContext), a repeat question is answered without kagent, and
/// another person's same question is not.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class AnswerCacheTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string EmployeeEmail = "rita@skanyxx.example";
    private const string EmployeePassword = "a long employee passphrase";

    private readonly IContainer _redis = new ContainerBuilder("redis:7-alpine")
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
        .Build();

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["ChatContext:AnswerCache:Enabled"] = "true";
            s["ChatContext:AnswerCache:Redis"] = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}";
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private static async Task<JsonElement> AskAsync(HttpClient client, string message, bool noCache = false)
    {
        var response = await client.PostAsJsonAsync("/api/chat",
            new { agentNamespace = "kagent", agentName = "seed", message, noCache }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ARepeatQuestion_IsAnsweredFromTheCache_OnlyForTheSamePerson()
    {
        var owner = await _host.OwnerAsync();
        var first = await AskAsync(owner, "What is our refund window?");
        Assert.False(first.GetProperty("cached").GetBoolean());

        var again = await AskAsync(owner, "what is our refund window");

        Assert.True(again.GetProperty("cached").GetBoolean());
        Assert.Equal(first.GetProperty("conversationId").GetString(), again.GetProperty("conversationId").GetString());
        Assert.Equal(_kagent.Answer, again.GetProperty("message").GetProperty("content").GetString());
        Assert.Single(_kagent.CallsTo("POST", "/api/a2a/kagent/seed/"));
        Assert.Single(_kagent.CallsTo("POST", "/api/sessions"));

        var employee = await EmployeeAsync();
        var theirs = await AskAsync(employee, "What is our refund window?");
        Assert.False(theirs.GetProperty("cached").GetBoolean());
        Assert.Equal(2, _kagent.CallsTo("POST", "/api/a2a/kagent/seed/").Count());

        var fresh = await AskAsync(owner, "What is our refund window?", noCache: true);
        Assert.False(fresh.GetProperty("cached").GetBoolean());
        Assert.Equal(3, _kagent.CallsTo("POST", "/api/a2a/kagent/seed/").Count());

        var stats = await owner.GetFromJsonAsync<JsonElement>("/api/chat/cache", TestContext.Current.CancellationToken);
        Assert.Equal(1, stats.GetProperty("hits").GetInt64());
        Assert.Equal(1, stats.GetProperty("allHits").GetInt64());
    }

    private async Task<HttpClient> EmployeeAsync()
    {
        var owner = await _host.OwnerAsync();
        var invite = await owner.PostAsJsonAsync("/api/identity/invites", new { email = EmployeeEmail, roles = new[] { "employee" } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;
        (await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = EmployeePassword })).EnsureSuccessStatusCode();
        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = EmployeeEmail, password = EmployeePassword });
        signIn.EnsureSuccessStatusCode();
        return _host.Client((await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }
}
