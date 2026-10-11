using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D076/D077: on the sandbox listener (Memory:SandboxMcpPort) a request is identified by its signed per-sandbox token
/// and nothing else — agent secrets and identity headers mean nothing there, and the token means nothing elsewhere.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SandboxTokenTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Key = "a-sandbox-token-key-of-at-least-32-characters";
    private const string AgentA = "ax-fix-42-0a1b2c3d";
    private const string AgentB = "ax-other-0e0f0a0b";

    private MemoryApp _app = null!;
    private int _main, _sandbox;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        (_main, _sandbox) = (FreePort(), FreePort());
        _app = await MemoryApp.StartAsync(postgres.ConnectionString, extra: new Dictionary<string, string?>
        {
            ["urls"] = $"http://127.0.0.1:{_main};http://127.0.0.1:{_sandbox}",
            ["Memory:SandboxMcpPort"] = _sandbox.ToString(),
            ["Memory:SandboxTokenKey"] = Key
        });
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static Dictionary<string, object?> Upsert(string key, string scope = "personal") => new()
    {
        ["key"] = key, ["type"] = "decision", ["what"] = "We refund within 14 days", ["why"] = "Finance policy X",
        ["version"] = 0, ["scope"] = scope
    };

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
        (result.StructuredContent is { } s ? JsonSerializer.Serialize(s) : "");

    private async Task<T> SendAsync<T>(IRequest<T> request)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    private async Task<string> IssueAsync(string agentId = AgentA, string sandbox = "fix-42", string owner = Users.Ana)
    {
        var issued = await SendAsync(new IssueSandboxMemoryTokenCommand(sandbox, agentId, owner));
        Assert.Equal(OutcomeStatus.Ok, issued.Status);
        return issued.Value!.Token;
    }

    private Task<HttpResponseMessage> ToolsListAsync(string? token, int? port = null, IDictionary<string, string>? headers = null) =>
        _app.PostMcpAsync("tools/list", token is null ? null : $"Bearer {token}", port: port ?? _sandbox, headers: headers);

    [Fact]
    public async Task AValidToken_OnTheSandboxListener_IsTheSandboxActingForItsOwner()
    {
        await _app.Client(Users.Ana).PutCardAsync($"personal:{Users.Ana}", "ana-refund");
        await _app.Client(Users.Bob).PutCardAsync($"personal:{Users.Bob}", "bob-refund");
        await _app.SupervisorClient().PutCardAsync("company", "company-refund");
        var token = await IssueAsync();

        // Whatever the sandbox claims in headers, it is its token's agent acting for its token's owner.
        await using var client = await _app.McpWithSecretAsync(token, userId: Users.Bob, port: _sandbox,
            headers: new Dictionary<string, string> { ["X-Agent-Id"] = "kagent-admin" });
        var search = Text(await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" },
            cancellationToken: TestContext.Current.CancellationToken));
        var upsert = await client.CallToolAsync("memory_upsert", Upsert("sandbox-note"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("ana-refund", search);
        Assert.Contains("company-refund", search);
        Assert.DoesNotContain("bob-refund", search);
        Assert.NotEqual(true, upsert.IsError);
        Assert.Contains($"personal:{Users.Ana}", Text(upsert));
        await using var db = postgres.CreateDbContext();
        Assert.Equal($"{Users.Ana} via {AgentA}", (await db.Cards.SingleAsync(c => c.Key == "sandbox-note", TestContext.Current.CancellationToken)).Who);
        Assert.DoesNotContain(token, _app.Logs);
    }

    [Fact]
    public async Task Grants_ApplyToTheTokensAgentId()
    {
        var tokenA = await IssueAsync();
        await IssueAsync(AgentB, "other", Users.Bob);
        await _app.SupervisorClient().SetGrantsAsync(AgentB, new { scope = "company", canSearch = true, canUpsert = true });

        await using var client = await _app.McpWithSecretAsync(tokenA, port: _sandbox,
            headers: new Dictionary<string, string> { ["X-Agent-Id"] = AgentB });
        var company = await client.CallToolAsync("memory_upsert", Upsert("company-note", "company"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(company.IsError);
        Assert.Contains("No upsert grant", Text(company));
        Assert.Equal(0, await postgres.CardCountAsync());
    }

    [Fact]
    public async Task NoToken_OrAnythingElse_Is401_OnTheSandboxListener()
    {
        var secret = await _app.IssueSecretAsync("kagent-agent", actsForUsers: true);
        var forged = await IssueWithKeyAsync("another-key-that-is-also-long-enough-to-sign");

        var none = await ToolsListAsync(null, headers: new Dictionary<string, string> { ["X-Agent-Id"] = AgentA, ["X-User-Id"] = Users.Ana });
        var agentSecret = await ToolsListAsync(secret);
        var garbage = await ToolsListAsync("skx_sbx_not-a-token.at-all");
        var wrongKey = await ToolsListAsync(forged);

        Assert.All(new[] { none, agentSecret, garbage, wrongKey }, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal("Bearer", Assert.Single(none.Headers.WwwAuthenticate).Scheme);
        Assert.Contains("SKANYXX_MEMORY_TOKEN", await none.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // The same agent secret still works where agent secrets belong.
        Assert.Equal(HttpStatusCode.OK, (await ToolsListAsync(secret, _main)).StatusCode);
    }

    [Fact]
    public async Task AToken_IsWorthNothing_OffTheSandboxListener()
    {
        var token = await IssueAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ToolsListAsync(token, _main)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ToolsListAsync(token)).StatusCode);
    }

    [Fact]
    public async Task RevokedReplacedOrExpired_Is401()
    {
        var revoked = await IssueAsync();
        var replaced = await IssueAsync(AgentB, "other", Users.Bob);
        var current = await IssueAsync(AgentB, "other", Users.Bob);
        var expired = await IssueAsync("ax-old-0c0c0c0c", "old");
        await using (var db = postgres.CreateDbContext())
            await db.SandboxTokens.Where(t => t.AgentId == "ax-old-0c0c0c0c")
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)), TestContext.Current.CancellationToken);

        var outcome = await SendAsync(new RevokeSandboxMemoryTokenCommand(AgentA));

        Assert.True(outcome.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ToolsListAsync(revoked)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ToolsListAsync(replaced)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ToolsListAsync(expired)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ToolsListAsync(current)).StatusCode);
        Assert.False((await SendAsync(new RevokeSandboxMemoryTokenCommand(AgentA))).Value);
    }

    [Fact]
    public async Task AnAgentId_BoundToAnotherSandboxOrOwner_IsNotRebound()
    {
        var token = await IssueAsync();

        var otherOwner = await SendAsync(new IssueSandboxMemoryTokenCommand("fix-42", AgentA, Users.Bob));
        var otherSandbox = await SendAsync(new IssueSandboxMemoryTokenCommand("fix-43", AgentA, Users.Ana));

        Assert.Equal(OutcomeStatus.Conflict, otherOwner.Status);
        Assert.Equal(OutcomeStatus.Conflict, otherSandbox.Status);
        Assert.Equal(HttpStatusCode.OK, (await ToolsListAsync(token)).StatusCode);
    }

    [Theory]
    [InlineData("kagent-agent")]
    [InlineData("Not An Id")]
    public async Task OnlySandboxAgentIds_GetATokens(string agentId) =>
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => SendAsync(new IssueSandboxMemoryTokenCommand("fix-42", agentId, Users.Ana)));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AnOwnerWhoLosesAccess_LosesEverySandboxToken(bool disabled, bool accessRemoved)
    {
        var anas = await IssueAsync();
        var bobs = await IssueAsync(AgentB, "other", Users.Bob);

        await using (var scope = _app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(
                new PrivilegesRevoked(Users.Ana, "account disabled", disabled, accessRemoved), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ToolsListAsync(anas)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ToolsListAsync(bobs)).StatusCode);
    }

    [Fact]
    public async Task LosingSupervisorAlone_KeepsTheTokens()
    {
        var token = await IssueAsync();

        await using (var scope = _app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(
                new PrivilegesRevoked(Users.Ana, "supervisor role removed"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, (await ToolsListAsync(token)).StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/api/memory/grants/ax-fix-42-0a1b2c3d")]
    [InlineData("/api/memory/cards")]
    [InlineData("/mcp")]
    [InlineData("/mcp/memoryx")]
    public async Task OnTheSandboxListener_EveryOtherPath_Is404(string path)
    {
        var token = await IssueAsync();
        var client = _app.Client(MemoryApp.Supervisor, SkanyxxRoles.Supervisor);
        client.BaseAddress = _app.At(_sandbox);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(path, null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task IssuingWithTheListenerOff_IsUnavailable()
    {
        await using var app = await MemoryApp.StartAsync(postgres.ConnectionString);
        await using var scope = app.Services.CreateAsyncScope();

        var issued = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new IssueSandboxMemoryTokenCommand("fix-42", AgentA, Users.Ana), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.Unavailable, issued.Status);
    }

    [Fact]
    public async Task RevokingWithTheDatabaseDown_IsUnavailable_NotAnException()
    {
        await using var app = MemoryApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2"
        });
        await using var scope = app.Services.CreateAsyncScope();

        var revoked = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new RevokeSandboxMemoryTokenCommand(AgentA), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.Unavailable, revoked.Status);
    }

    [Fact]
    public void TheIssuedToken_IsNeverPrinted() =>
        Assert.DoesNotContain("secret-value", new SandboxMemoryToken(AgentA, "secret-value", DateTimeOffset.UtcNow).ToString());

    // A token signed the right way but with another key, and given a live row, must still fail on the signature.
    private async Task<string> IssueWithKeyAsync(string key)
    {
        var token = SandboxTokenCodec.Sign(new("fix-9", "ax-fix-9-01010101", Users.Ana, DateTimeOffset.UtcNow.AddHours(1)), key);
        await using var db = postgres.CreateDbContext();
        db.SandboxTokens.Add(new SandboxToken
        {
            AgentId = "ax-fix-9-01010101", SandboxName = "fix-9", OwnerId = Users.Ana, TokenHash = SandboxTokenCodec.Hash(token),
            ExpiresAt = DateTime.UtcNow.AddHours(1), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return token;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
