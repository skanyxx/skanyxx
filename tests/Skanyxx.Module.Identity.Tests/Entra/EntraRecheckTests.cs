using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>
/// D158: between Microsoft sign-ins, the re-check asks Graph which mapped groups each Entra-managed account is in and
/// applies it — re-mapped (sessions end on a role change, a lost supervisor is revoked), or refused like a sign-in when
/// no mapped group is left (D2). Graph unreachable, Microsoft sign-in off, the owner, disabled accounts and other
/// tenants' keys: nothing changes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EntraRecheckTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Supervisors = "00000000-0000-0000-0000-0000000000a1";
    private const string Builders = "00000000-0000-0000-0000-0000000000a2";
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly SwitchableGraph _graph = new();
    private readonly Revocations _revoked = new();
    private readonly WarningLog _log = new();
    private IdentityApp _app = null!;
    private string _owner = null!;
    private string _memberId = null!;
    private Tokens _member = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s
            .AddSingleton<IGraphMembership>(_graph)
            .AddSingleton<INotificationHandler<PrivilegesRevoked>>(_revoked)
            .AddSingleton<ILoggerProvider>(_log));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
        _owner = (await _app.SignInBearerAsync()).AccessToken;
        Assert.Equal(HttpStatusCode.Created, (await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "eng", name = "Engineering" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "web", name = "Web", department = "eng" })).StatusCode);
        await SaveAsync(enabled: true);
        (_member, _memberId) = await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);
        Assert.Equal(HttpStatusCode.OK, (await Owner().PutAsync($"/api/identity/org/teams/web/members/{_memberId}", null)).StatusCode);
        await AddLoginAsync(_memberId, $"{EntraSettingsTests.Tenant}|{Oid:D}");
        _graph.Groups = [Supervisors];
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task StillInTheGroups_ChangesNothing()
    {
        var changed = await RunAsync();

        Assert.Equal(0, changed);
        var call = Assert.Single(_graph.Calls);
        Assert.Equal(Oid, call.ObjectId);
        Assert.Equal([Supervisors, Builders], call.Asked.Order()); // the mapped ids only
        Assert.Equal(HttpStatusCode.OK, (await Member().GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty(await postgres.AuditAsync(AuditActions.EntraRecheckRemapped));
        Assert.Empty(_revoked.Received);
    }

    [Fact]
    public async Task MovedToAnotherGroup_IsRemapped_EndsItsSessions_AndRevokesWhatItIssued()
    {
        _graph.Groups = [Builders];

        var changed = await RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal(["builder"], await RolesAsync(_memberId));
        Assert.Empty(await TeamsAsync(_memberId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Member().GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken)).StatusCode);
        var row = Assert.Single(await postgres.AuditAsync(AuditActions.EntraRecheckRemapped));
        Assert.Equal(_memberId, row.TargetId);
        Assert.Equal(EntraScheme.Actor, row.ActorId);
        Assert.Equal([new PrivilegesRevoked(_memberId, "Entra group re-check without supervisor")], _revoked.Received);
    }

    /// <summary>No mapped group any more (or the user is gone from the tenant: Graph's 404 is "no groups"): a refused sign-in, D2.</summary>
    [Fact]
    public async Task NoMappedGroupLeft_IsRefusedLikeASignIn_AndOnlyOnce()
    {
        _graph.Groups = [];

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Empty(await RolesAsync(_memberId));
        Assert.Empty(await TeamsAsync(_memberId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Member().GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Single(await postgres.AuditAsync(AuditActions.EntraRecheckRefused));
        await using var db = postgres.CreateDbContext();
        Assert.Null((await db.Users.SingleAsync(u => u.Id == _memberId, cancellationToken: TestContext.Current.CancellationToken)).LockoutEnd); // the account stays, enabled
        // D161: the refusal also stops what runs in the person's name (sandboxes honour AccessRemoved), once.
        var revoked = Assert.Single(_revoked.Received);
        Assert.True(revoked.AccessRemoved);
        Assert.False(revoked.AccountDisabled);
    }

    /// <summary>
    /// CR L2 + D161: a person whose mapping granted no roles or teams, removed from every mapped group, still loses their
    /// sessions and their sandbox tasks — the first refusal acts even when there is nothing to take away.
    /// </summary>
    [Fact]
    public async Task ARefusedAccount_WithNothingToRemove_StillLosesItsSessionsAndTasks()
    {
        await using (var db = postgres.CreateDbContext())
        {
            await db.UserRoles.Where(r => r.UserId == _memberId).ExecuteDeleteAsync(cancellationToken: TestContext.Current.CancellationToken);
            await db.TeamMembers.Where(m => m.UserId == _memberId).ExecuteDeleteAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        Assert.Equal(HttpStatusCode.OK, (await Member().GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken)).StatusCode);
        _graph.Groups = [];

        var changed = await RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Member().GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.RefreshAsync(_member.RefreshToken)).StatusCode);
        Assert.True(Assert.Single(_revoked.Received).AccessRemoved);
        Assert.Contains("sandbox tasks stopped", Assert.Single(await postgres.AuditAsync(AuditActions.EntraRecheckRefused)).Details);
    }

    /// <summary>D161: back in a mapped group clears the refusal, so a later removal acts again.</summary>
    [Fact]
    public async Task BackInAGroup_ClearsTheRefusal_SoALaterRemovalActsAgain()
    {
        _graph.Groups = [];
        await RunAsync();
        _graph.Groups = [Supervisors];
        var back = await RunAsync();
        // A stale mark would make any later revocation of this active person stop their tasks.
        await using (var marks = postgres.CreateDbContext())
            Assert.Empty(await marks.UserTokens.Where(t => t.UserId == _memberId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
        _graph.Groups = [];
        var again = await RunAsync();

        Assert.Equal(1, back);
        Assert.Equal(1, again);
        Assert.Equal([true, true], _revoked.Received.Select(r => r.AccessRemoved));
        await using var db = postgres.CreateDbContext();
        Assert.Single(await db.UserTokens.Where(t => t.UserId == _memberId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>D161: a revocation that failed is retried by the next sweep, and still says the access is gone.</summary>
    [Fact]
    public async Task AFailedStop_IsRetriedByTheNextSweep_StillAsAccessRemoved()
    {
        _graph.Groups = [];
        _revoked.FailNext = true;

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var retried = Assert.Single(_revoked.Received);
        Assert.True(retried.AccessRemoved);
        Assert.Equal(PrivilegeRevocationRetried, retried.Reason);
    }

    /// <summary>
    /// QA-2 L2: turning Microsoft sign-in off clears every re-check refusal in the save, so the person — who signs in
    /// with a password again (D8) — keeps their sandbox tasks when a later save takes supervisor away.
    /// </summary>
    [Fact]
    public async Task TurningSignInOff_ClearsTheRefusal_SoALaterRevocationLeavesTheTasks()
    {
        _graph.Groups = [];
        await RunAsync();

        await SaveAsync(enabled: false);
        var roles = await Owner().PutAsJsonAsync($"/api/identity/people/{_memberId}/roles", new { roles = new[] { SkanyxxRoles.Builder } }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Equal([true, false], _revoked.Received.Select(r => r.AccessRemoved));
        Assert.Equal(0, await RefusalMarksAsync());
    }

    /// <summary>QA-2 L2: a new tenant clears the refusals the old one made (its keys are no longer re-checked), sign-in staying on.</summary>
    [Fact]
    public async Task ChangingTheTenant_ClearsTheRefusal()
    {
        _graph.Groups = [];
        await RunAsync();

        await SaveAsync(enabled: true, tenant: "44444444-4444-4444-4444-444444444444");
        await Owner().PutAsJsonAsync($"/api/identity/people/{_memberId}/roles", new { roles = new[] { SkanyxxRoles.Builder } }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([true, false], _revoked.Received.Select(r => r.AccessRemoved));
        Assert.Equal(0, await RefusalMarksAsync());
    }

    /// <summary>
    /// QA-2 L2: a mark is honoured only while Microsoft sign-in can be used — one written after the save turned it off
    /// (a sweep already under way) does not stop the tasks of someone who signs in with a password.
    /// </summary>
    [Fact]
    public async Task AMarkLeftWhileSignInIsOff_IsNotHonoured()
    {
        await SaveAsync(enabled: false);
        await using (var db = postgres.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity_user_tokens ("UserId", "LoginProvider", "Name", "Value") VALUES ({_memberId}, {EntraScheme.Name}, {EntraRefusal.Name}, 'x')
                """, cancellationToken: TestContext.Current.CancellationToken);

        await Owner().PutAsJsonAsync($"/api/identity/people/{_memberId}/roles", new { roles = new[] { SkanyxxRoles.Builder } }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(_revoked.Received).AccessRemoved);
    }

    /// <summary>CR L6 / D164: one sweep per interval across replicas, whatever their timers do; a stale claim is due again.</summary>
    [Fact]
    public async Task ASecondRunWithinTheInterval_DoesNotSweep_AnOldClaimDoes()
    {
        var first = await _app.Services.GetRequiredService<EntraRecheck>().RunOnceAsync(CancellationToken.None);
        var second = await _app.Services.GetRequiredService<EntraRecheck>().RunOnceAsync(CancellationToken.None);
        await using (var db = postgres.CreateDbContext())
            await db.JobRuns.ExecuteUpdateAsync(s => s.SetProperty(r => r.LastRunUtc, r => r.LastRunUtc.AddMinutes(-61)), cancellationToken: TestContext.Current.CancellationToken);
        var third = await _app.Services.GetRequiredService<EntraRecheck>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, first);
        Assert.Null(second);
        Assert.Equal(0, third);
        Assert.Equal(2, _graph.Calls.Count);
    }

    /// <summary>
    /// QA-2: the claim is timed by the database's clock. A replica whose clock runs two hours ahead claims a sweep; once
    /// that claim is an interval old by the database, a replica with a right clock claims the next one.
    /// </summary>
    [Fact]
    public async Task TheClaim_UsesTheDatabasesClock_NotTheReplicas()
    {
        var ahead = new ManualClock();
        ahead.Advance(TimeSpan.FromHours(2));
        await using var skewed = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s
            .AddSingleton<TimeProvider>(ahead)
            .AddSingleton<IGraphMembership>(_graph)
            .AddSingleton<INotificationHandler<PrivilegesRevoked>>(_revoked));
        await using (var db = postgres.CreateDbContext())
            await db.JobRuns.ExecuteDeleteAsync(cancellationToken: TestContext.Current.CancellationToken);

        var first = await skewed.Services.GetRequiredService<EntraRecheck>().RunOnceAsync(CancellationToken.None);
        await using (var db = postgres.CreateDbContext())
            await db.JobRuns.ExecuteUpdateAsync(s => s.SetProperty(r => r.LastRunUtc, r => r.LastRunUtc.AddMinutes(-61)), cancellationToken: TestContext.Current.CancellationToken);
        var next = await _app.Services.GetRequiredService<EntraRecheck>().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, first);
        Assert.Equal(0, next);
    }

    /// <summary>CR L6: a missing consent (403) or a stale group id (400) is an Error naming the cause, not a quiet Warning.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "GroupMember.Read.All")]
    [InlineData(HttpStatusCode.BadRequest, "stale")]
    public async Task GraphRefusingTheApp_IsAnError_NamingTheCause(HttpStatusCode status, string cause)
    {
        _graph.Fail = new HttpRequestException("refused", null, status);

        var changed = await RunAsync();

        Assert.Equal(0, changed);
        Assert.Single(_log.Errors, e => e.Contains(cause));
        Assert.Equal(["supervisor"], await RolesAsync(_memberId));
    }

    [Fact]
    public async Task GraphUnreachable_ChangesNothing_AndWarns()
    {
        _graph.Fail = new HttpRequestException("graph.microsoft.com is down");
        _graph.Groups = [];

        var changed = await RunAsync();

        Assert.Equal(0, changed);
        Assert.Equal(["supervisor"], await RolesAsync(_memberId));
        Assert.Equal(HttpStatusCode.OK, (await Member().GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Contains(_log.Warnings, w => w.Contains("could not ask Graph"));
    }

    [Fact]
    public async Task WithMicrosoftSignInOff_NothingIsAsked()
    {
        await SaveAsync(enabled: false);
        _graph.Groups = [];

        var changed = await RunAsync();

        Assert.Null(changed);
        Assert.Empty(_graph.Calls);
        Assert.Equal(["supervisor"], await RolesAsync(_memberId));
    }

    [Fact]
    public async Task TheOwner_DisabledAccounts_AndOtherTenants_AreLeftAlone()
    {
        var ownerId = (await Owner().GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
        await AddLoginAsync(ownerId, $"{EntraSettingsTests.Tenant}|aaaaaaaa-0000-0000-0000-000000000002");
        var (_, disabledId) = await _app.AddMemberAsync(_owner, "disabled@skanyxx.example", SkanyxxRoles.Builder);
        await AddLoginAsync(disabledId, $"{EntraSettingsTests.Tenant}|aaaaaaaa-0000-0000-0000-000000000003");
        await Owner().PostAsync($"/api/identity/people/{disabledId}/disable", null, TestContext.Current.CancellationToken);
        var (_, foreignId) = await _app.AddMemberAsync(_owner, "foreign@skanyxx.example", SkanyxxRoles.Builder);
        await AddLoginAsync(foreignId, "33333333-3333-3333-3333-333333333333|aaaaaaaa-0000-0000-0000-000000000004");
        _graph.Groups = [];

        await RunAsync();

        Assert.Equal([Oid], _graph.Calls.Select(c => c.ObjectId));
        Assert.Equal(["builder"], await RolesAsync(disabledId));
        Assert.Equal(["builder"], await RolesAsync(foreignId));
        Assert.Contains(_log.Warnings, w => w.Contains("another tenant"));
    }

    /// <summary>One replica sweeps at a time: while another holds the sweep lock, this one does not run.</summary>
    [Fact]
    public async Task AnotherReplicaSweeping_SkipsThisRun()
    {
        await using var other = postgres.CreateDbContext();
        await using var held = await other.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await other.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({EntraRecheck.SweepLockKey})", cancellationToken: TestContext.Current.CancellationToken);

        var changed = await RunAsync();

        Assert.Null(changed);
        Assert.Empty(_graph.Calls);
    }

    private const string PrivilegeRevocationRetried = "an earlier revocation retried";

    /// <summary>A sweep now, whenever the last one ran (the claim row cleared first, D164).</summary>
    private async Task<int?> RunAsync()
    {
        await using (var db = postgres.CreateDbContext())
            await db.JobRuns.ExecuteDeleteAsync();
        return await _app.Services.GetRequiredService<EntraRecheck>().RunOnceAsync(CancellationToken.None);
    }

    private HttpClient Owner() => _app.Client(bearer: _owner);

    private HttpClient Member() => _app.Client(bearer: _member.AccessToken);

    private async Task SaveAsync(bool enabled, string tenant = EntraSettingsTests.Tenant)
    {
        var response = await Owner().PutAsJsonAsync("/api/identity/entra/settings", EntraSettingsTests.Settings(enabled: enabled, tenant: tenant, groups:
        [
            EntraSettingsTests.Map(Supervisors, "Skanyxx supervisors", [SkanyxxRoles.Supervisor], ["web"]),
            EntraSettingsTests.Map(Builders, "Skanyxx builders", [SkanyxxRoles.Builder], [])
        ]));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task AddLoginAsync(string userId, string key)
    {
        await using var db = postgres.CreateDbContext();
        db.UserLogins.Add(new IdentityUserLogin<string> { LoginProvider = EntraScheme.Name, ProviderKey = key, ProviderDisplayName = "Microsoft", UserId = userId });
        await db.SaveChangesAsync();
    }

    private async Task<int> RefusalMarksAsync()
    {
        await using var db = postgres.CreateDbContext();
        return await db.UserTokens.CountAsync(t => t.LoginProvider == EntraScheme.Name && t.Name == EntraRefusal.Name);
    }

    private async Task<List<string>> RolesAsync(string userId)
    {
        await using var db = postgres.CreateDbContext();
        return await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleId).OrderBy(r => r).ToListAsync();
    }

    private async Task<List<string>> TeamsAsync(string userId)
    {
        await using var db = postgres.CreateDbContext();
        return await db.TeamMembers.Where(m => m.UserId == userId).Select(m => m.TeamSlug).ToListAsync();
    }

    /// <summary>Graph whose answer the test changes between runs.</summary>
    private sealed class SwitchableGraph : IGraphMembership
    {
        public string[] Groups { get; set; } = [];

        public Exception? Fail { get; set; }

        public ConcurrentQueue<(Guid ObjectId, IReadOnlyCollection<string> Asked)> Calls { get; } = new();

        public Task<IReadOnlySet<string>> MemberOfAsync(EntraConfig config, Guid objectId, IReadOnlyCollection<string> groupIds, CancellationToken ct)
        {
            Calls.Enqueue((objectId, groupIds));
            return Fail is not null
                ? Task.FromException<IReadOnlySet<string>>(Fail)
                : Task.FromResult<IReadOnlySet<string>>(groupIds.Intersect(Groups).ToHashSet());
        }
    }

    private sealed class Revocations : INotificationHandler<PrivilegesRevoked>
    {
        private readonly ConcurrentQueue<PrivilegesRevoked> _received = new();

        public IReadOnlyList<PrivilegesRevoked> Received => [.. _received];

        public volatile bool FailNext;

        public Task Handle(PrivilegesRevoked notification, CancellationToken ct)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new RevocationFailedException("Its sandbox tasks could not be stopped (AX unavailable).");
            }
            _received.Enqueue(notification);
            return Task.CompletedTask;
        }
    }
}
