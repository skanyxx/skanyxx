using System.Net;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// The studio reconciler's memory contracts (slice 3, D110): grants from git, a studio-issued secret that never acts for
/// users, removal — and the owner's acts-for-users agents are never the studio's to touch (D084).
/// </summary>
public sealed class StudioAccessTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private const string Actor = "studio:reconcile";

    private async Task<T> SendAsync<T>(IRequest<T> request)
    {
        using var scope = App.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    private async Task<T> DbAsync<T>(Func<MemoryDbContext, Task<T>> read)
    {
        using var scope = App.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<MemoryDbContext>());
    }

    private static Dictionary<string, object?> Upsert(string key, string scope) => new()
    {
        ["key"] = key, ["type"] = "fact", ["what"] = "A draft agent wrote this", ["why"] = "It must not", ["version"] = 0, ["scope"] = scope
    };

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
        (result.StructuredContent is { } s ? JsonSerializer.Serialize(s) : "");

    [Fact]
    public async Task Grants_AreReplaced_OnlyWhenTheyDiffer()
    {
        StudioGrant[] grants = [new("company", true, false), new("team:billing", true, true)];

        var first = await SendAsync(new SetStudioGrantsCommand(Actor, "refund-helper", grants));
        var again = await SendAsync(new SetStudioGrantsCommand(Actor, "refund-helper", [.. grants.Reverse()]));
        var changed = await SendAsync(new SetStudioGrantsCommand(Actor, "refund-helper", [new("company", true, true)]));

        Assert.Equal((OutcomeStatus.Ok, true), (first.Status, first.Value));
        Assert.Equal((OutcomeStatus.Ok, false), (again.Status, again.Value));
        Assert.True(changed.Value);
        var stored = await DbAsync(db => db.Grants.Where(g => g.AgentId == "refund-helper").ToListAsync());
        Assert.Equal(("company", true, true), Assert.Single(stored) is var g ? (g.Scope, g.CanSearch, g.CanUpsert) : default);
        Assert.Contains("Agent memory grants set for refund-helper by the studio for studio:reconcile", App.Logs);
    }

    [Fact]
    public async Task AStudioSecret_IsTheStudios_NeverActsForUsers_AndSearchesItsGrantsOnly()
    {
        await App.SupervisorClient().PutCardAsync("company", "refund-window");
        await SendAsync(new SetStudioGrantsCommand(Actor, "refund-helper", [new("company", true, false)]));

        var issued = await SendAsync(new IssueStudioSecretCommand(Actor, "refund-helper"));
        await using var client = await App.McpWithSecretAsync(issued.Value!.Secret, userId: "someone-else");
        var search = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" }, cancellationToken: TestContext.Current.CancellationToken);
        var upsert = await client.CallToolAsync("memory_upsert", Upsert("draft-note", "company"), cancellationToken: TestContext.Current.CancellationToken);

        var row = await DbAsync(db => db.AgentSecrets.SingleAsync(s => s.AgentId == "refund-helper"));
        Assert.Equal((StudioSecret.IssuedBy, false), (row.CreatedBy, row.ActsForUsers));
        Assert.Equal(issued.Value.Fingerprint, (await SendAsync(new StudioPrincipalQuery("refund-helper"))).Value!.SecretFingerprint);
        Assert.Contains("refund-window", Text(search));
        Assert.True(upsert.IsError);
        Assert.Contains("No upsert grant", Text(upsert));
        Assert.Equal(0, await DbAsync(db => db.Cards.CountAsync(c => c.Key == "draft-note")));
        Assert.DoesNotContain(issued.Value.Secret, issued.ToString());
        Assert.DoesNotContain(issued.Value.Secret, App.Logs);
    }

    [Fact]
    public async Task APersonsRotation_IsNotTheStudiosSecret()
    {
        await SendAsync(new IssueStudioSecretCommand(Actor, "refund-helper"));
        await App.IssueSecretAsync("refund-helper");

        var principal = (await SendAsync(new StudioPrincipalQuery("refund-helper"))).Value!;
        Assert.Null(principal.SecretFingerprint);
        Assert.False(principal.Taken); // still the studio's: its next pass issues again
    }

    /// <summary>
    /// M5 (D117): a principal someone made by hand — a normal secret and grants through REST, no acts-for-users — is not
    /// the studio's to take over: a studio agent of the same name would replace its grants (team ones without the owner)
    /// and cut the real one off.
    /// </summary>
    [Fact]
    public async Task AnOwnerMadePrincipal_IsNeverTakenOverByTheStudio()
    {
        var secret = await App.IssueSecretAsync("foo", byOwner: true);
        await App.OwnerClient().SetGrantsAsync("foo", new { scope = "team:billing", canSearch = true, canUpsert = false });
        await App.OwnerClient().SetGrantsAsync("grants-only", new { scope = "company", canSearch = true, canUpsert = false });

        var grants = await SendAsync(new SetStudioGrantsCommand(Actor, "foo", [new("company", true, true)]));
        var issue = await SendAsync(new IssueStudioSecretCommand(Actor, "foo"));
        var remove = await SendAsync(new RemoveStudioAccessCommand(Actor, "foo"));
        var mark = await SendAsync(new MarkStudioSecretDeployedCommand(Actor, "foo", "abc123"));
        var grantsOnly = await SendAsync(new IssueStudioSecretCommand(Actor, "grants-only"));

        Assert.All([grants.Status, issue.Status, remove.Status, mark.Status, grantsOnly.Status], s => Assert.Equal(OutcomeStatus.Forbidden, s));
        Assert.Contains("did not create", issue.Message);
        Assert.True((await SendAsync(new StudioPrincipalQuery("foo"))).Value!.Taken);
        Assert.True((await SendAsync(new StudioPrincipalQuery("grants-only"))).Value!.Taken);
        Assert.Equal("team:billing", (await DbAsync(db => db.Grants.SingleAsync(g => g.AgentId == "foo"))).Scope);
        Assert.Equal(MemoryApp.Owner, (await DbAsync(db => db.AgentSecrets.SingleAsync(s => s.AgentId == "foo"))).CreatedBy);
        Assert.Equal(0, await DbAsync(db => db.StudioAgents.CountAsync(a => a.AgentId == "foo" || a.AgentId == "grants-only")));
        await using var client = await App.McpWithSecretAsync(secret);
        Assert.NotEqual(true, (await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "x" }, cancellationToken: TestContext.Current.CancellationToken)).IsError);
    }

    /// <summary>A free name is claimed by the studio's first write and given back when its access is removed.</summary>
    [Fact]
    public async Task AFreeName_IsClaimed_ThenGivenBack()
    {
        Assert.False((await SendAsync(new StudioPrincipalQuery("fresh"))).Value!.Taken);
        Assert.Equal((OutcomeStatus.Ok, false), await SendAsync(new RemoveStudioAccessCommand(Actor, "fresh")) is var none ? (none.Status, none.Value) : default);
        Assert.Equal(0, await DbAsync(db => db.StudioAgents.CountAsync(a => a.AgentId == "fresh")));

        await SendAsync(new SetStudioGrantsCommand(Actor, "fresh", [new("company", true, false)]));
        Assert.Equal(1, await DbAsync(db => db.StudioAgents.CountAsync(a => a.AgentId == "fresh")));
        await SendAsync(new RemoveStudioAccessCommand(Actor, "fresh"));

        Assert.Equal(0, await DbAsync(db => db.StudioAgents.CountAsync(a => a.AgentId == "fresh")));
        await App.IssueSecretAsync("fresh", byOwner: true); // the owner may take the name now
        Assert.True((await SendAsync(new StudioPrincipalQuery("fresh"))).Value!.Taken);
    }

    /// <summary>D118: memory knows which secret kagent holds; a new issue is not in place until it is marked.</summary>
    [Fact]
    public async Task TheDeployedMark_FollowsEachIssue()
    {
        var first = (await SendAsync(new IssueStudioSecretCommand(Actor, "refund-helper"))).Value!;
        Assert.False((await SendAsync(new StudioPrincipalQuery("refund-helper"))).Value!.SecretInPlace);

        Assert.True((await SendAsync(new MarkStudioSecretDeployedCommand(Actor, "refund-helper", first.Fingerprint))).Value);
        Assert.True((await SendAsync(new StudioPrincipalQuery("refund-helper"))).Value!.SecretInPlace);

        var second = (await SendAsync(new IssueStudioSecretCommand(Actor, "refund-helper"))).Value!;
        var principal = (await SendAsync(new StudioPrincipalQuery("refund-helper"))).Value!;
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.Equal((second.Fingerprint, first.Fingerprint), (principal.SecretFingerprint, principal.DeployedFingerprint));
        Assert.False(principal.SecretInPlace);
        Assert.Matches("^[a-f0-9]{12}$", second.Fingerprint);
        Assert.DoesNotContain(second.Secret, second.ToString());
    }

    /// <summary>D121: the owner's stop revokes at once, and nothing the studio does brings the agent back until resumed.</summary>
    [Fact]
    public async Task Suspending_RevokesAtOnce_AndKeepsItRevoked_UntilResumed()
    {
        await SendAsync(new SetStudioGrantsCommand(Actor, "refund-helper", [new("company", true, false)]));
        var issued = (await SendAsync(new IssueStudioSecretCommand(Actor, "refund-helper"))).Value!;

        var suspended = await SendAsync(new SuspendStudioAgentCommand("owner-id", "refund-helper", true));
        var grants = await SendAsync(new SetStudioGrantsCommand(Actor, "refund-helper", [new("company", true, false)]));
        var issue = await SendAsync(new IssueStudioSecretCommand(Actor, "refund-helper"));
        var remove = await SendAsync(new RemoveStudioAccessCommand(Actor, "refund-helper"));

        Assert.True(suspended.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.PostMcpAsync("tools/list", $"Bearer {issued.Secret}")).StatusCode);
        Assert.Equal((OutcomeStatus.Forbidden, OutcomeStatus.Forbidden), (grants.Status, issue.Status));
        Assert.Contains("suspended", issue.Message);
        Assert.Equal(OutcomeStatus.Ok, remove.Status);
        Assert.True((await SendAsync(new StudioPrincipalQuery("refund-helper"))).Value!.Suspended); // removal keeps the stop
        Assert.Equal(0, await DbAsync(db => db.Grants.CountAsync(g => g.AgentId == "refund-helper")));
        Assert.Contains("Studio agent refund-helper suspended", App.Logs);

        Assert.True((await SendAsync(new SuspendStudioAgentCommand("owner-id", "refund-helper", false))).Value);
        Assert.Equal(OutcomeStatus.Ok, (await SendAsync(new IssueStudioSecretCommand(Actor, "refund-helper"))).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await SendAsync(new SuspendStudioAgentCommand("owner-id", "never-seen", false))).Status);
        await App.IssueSecretAsync("owned", byOwner: true);
        Assert.Equal(OutcomeStatus.Forbidden, (await SendAsync(new SuspendStudioAgentCommand("owner-id", "owned", true))).Status);
    }

    /// <summary>D120: one holder across connections (so across replicas); the next one gets it once released.</summary>
    [Fact]
    public async Task TheReconcileLock_HasOneHolder()
    {
        var locks = App.Services.GetRequiredService<IStudioReconcileLock>();

        var first = await locks.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None);
        var second = await locks.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None);
        var waited = await locks.TryAcquireAsync(TimeSpan.FromMilliseconds(600), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Null(waited);
        await first.DisposeAsync();
        await using var third = await locks.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(third);
    }

    /// <summary>Round 2 m2: a lock whose connection broke releases without throwing (the pass's own outcome stands).</summary>
    [Fact]
    public async Task TheReconcileLock_ReleasesQuietly_WhenItsConnectionBroke()
    {
        var locks = App.Services.GetRequiredService<IStudioReconcileLock>();
        var held = (await locks.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None))!;
        var connection = (Npgsql.NpgsqlConnection)held.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Npgsql.NpgsqlConnection)).GetValue(held)!;
        await connection.CloseAsync(); // the unlock now meets "Connection is not open" (InvalidOperationException)

        await held.DisposeAsync();

        Assert.Contains("the unlock failed", App.Logs);
        await using var next = await locks.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task TheRepoRecord_IsOneRow_ReplacedOnConfirmation()
    {
        Assert.Null((await SendAsync(new StudioRepoQuery())).Value);

        await SendAsync(new RecordStudioRepoCommand(Actor, new StudioRepoIdentity(7, "2026-10-06T10:00:00Z", "skanyxx/skanyxx-agents")));
        await SendAsync(new RecordStudioRepoCommand("owner-id", new StudioRepoIdentity(9, "2026-10-07T10:00:00Z", "skanyxx/skanyxx-agents")));

        Assert.Equal(new StudioRepoIdentity(9, "2026-10-07T10:00:00Z", "skanyxx/skanyxx-agents"), (await SendAsync(new StudioRepoQuery())).Value);
        Assert.Equal(1, await DbAsync(db => db.StudioRepos.CountAsync()));
    }

    [Fact]
    public async Task AnActsForUsersAgent_IsNeverTouchedByTheStudio()
    {
        var secret = await App.IssueSecretAsync("seed", actsForUsers: true, byOwner: true);
        await App.OwnerClient().SetGrantsAsync("seed", new { scope = "company", canSearch = true, canUpsert = true });

        var grants = await SendAsync(new SetStudioGrantsCommand(Actor, "seed", [new("company", true, false)]));
        var issue = await SendAsync(new IssueStudioSecretCommand(Actor, "seed"));
        var remove = await SendAsync(new RemoveStudioAccessCommand(Actor, "seed"));

        Assert.All([grants.Status, issue.Status, remove.Status], s => Assert.Equal(OutcomeStatus.Forbidden, s));
        var row = await DbAsync(db => db.AgentSecrets.SingleAsync(s => s.AgentId == "seed"));
        Assert.True(row.ActsForUsers);
        Assert.Equal(MemoryApp.Owner, row.CreatedBy);
        Assert.True((await DbAsync(db => db.Grants.SingleAsync(g => g.AgentId == "seed"))).CanUpsert);
        await using var client = await App.McpWithSecretAsync(secret);
        Assert.NotEqual(true, (await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "x" }, cancellationToken: TestContext.Current.CancellationToken)).IsError);
    }

    [Fact]
    public async Task Remove_RevokesTheSecret_AndTheGrants()
    {
        await SendAsync(new SetStudioGrantsCommand(Actor, "preview-3-refund", [new("company", true, false)]));
        var issued = await SendAsync(new IssueStudioSecretCommand(Actor, "preview-3-refund"));

        var removed = await SendAsync(new RemoveStudioAccessCommand(Actor, "preview-3-refund"));
        var again = await SendAsync(new RemoveStudioAccessCommand(Actor, "preview-3-refund"));

        Assert.Equal((true, false), (removed.Value, again.Value));
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.PostMcpAsync("tools/list", $"Bearer {issued.Value!.Secret}")).StatusCode);
        Assert.Equal(0, await DbAsync(db => db.Grants.CountAsync(g => g.AgentId == "preview-3-refund")));
    }

    /// <summary>A studio agent acts for nobody: a personal scope (or a malformed one) is refused before any write.</summary>
    [Theory]
    [InlineData("personal")]
    [InlineData("personal:ana")]
    [InlineData("billing")]
    [InlineData("team:")]
    public async Task Grants_RefuseScopesAStudioAgentCannotHave(string scope)
    {
        await Assert.ThrowsAsync<ValidationException>(() => SendAsync(new SetStudioGrantsCommand(Actor, "refund-helper", [new(scope, true, false)])));
        await Assert.ThrowsAsync<ValidationException>(() => SendAsync(new SetStudioGrantsCommand(Actor, "Not An Id", [new("company", true, false)])));
    }
}
