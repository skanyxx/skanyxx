using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// D152/D155: every audited identity change writes a row in the transaction that commits it — a failed change leaves
/// none —, rows never hold a secret, only the owner reads them, they cannot be changed, and the retention job removes
/// old rows and finished invites.
/// </summary>
public sealed class AuditTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    private string _owner = null!;
    private string _ownerId = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
        _owner = (await App.SignInBearerAsync()).AccessToken;
        _ownerId = (await Owner().GetFromJsonAsync<JsonElement>("/api/identity/me")).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task PeopleInvitesAndOrg_AreAudited_WithActorTargetAndAddress_AndNoSecret()
    {
        var token = await App.InviteAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder);
        var accepted = await App.AcceptAsync(token);
        var memberId = (await accepted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("user").GetProperty("id").GetString()!;
        var revoked = await App.InviteAsync(_owner, "later@skanyxx.example");
        var inviteId = (await Owner().GetFromJsonAsync<JsonElement>("/api/identity/invites", cancellationToken: TestContext.Current.CancellationToken))[0].GetProperty("id").GetString()!;
        await Owner().DeleteAsync($"/api/identity/invites/{inviteId}", TestContext.Current.CancellationToken);
        await Owner().PutAsJsonAsync($"/api/identity/people/{memberId}/roles", new { roles = new[] { SkanyxxRoles.Employee } }, cancellationToken: TestContext.Current.CancellationToken);
        await Owner().PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        await Owner().PostAsync($"/api/identity/people/{memberId}/enable", null, TestContext.Current.CancellationToken);
        await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "eng", name = "Engineering" }, cancellationToken: TestContext.Current.CancellationToken);
        await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug = "web", name = "Web", department = "eng" }, cancellationToken: TestContext.Current.CancellationToken);
        await Owner().PutAsync($"/api/identity/org/teams/web/members/{memberId}", null, TestContext.Current.CancellationToken);
        await Owner().DeleteAsync($"/api/identity/org/teams/web/members/{memberId}", TestContext.Current.CancellationToken);
        await App.AcceptAsync("skx_inv_not-a-real-token");

        var rows = await Postgres.AuditAsync();
        string[] expected =
        [
            AuditActions.InviteCreated, AuditActions.InviteAccepted, AuditActions.InviteCreated, AuditActions.InviteRevoked,
            AuditActions.RolesChanged, AuditActions.Disabled, AuditActions.Enabled, AuditActions.DepartmentCreated, AuditActions.TeamCreated,
            AuditActions.MemberAdded, AuditActions.MemberRemoved, AuditActions.InviteAcceptRefused
        ];
        Assert.Equal(expected, rows.Select(r => r.Action));
        Assert.All(rows.Take(rows.Count - 1).Where(r => r.Action != AuditActions.InviteAccepted), r => Assert.Equal(_ownerId, r.ActorId));
        Assert.Equal(memberId, rows[1].ActorId);
        Assert.All(rows.Where(r => r.Action is AuditActions.RolesChanged or AuditActions.Disabled or AuditActions.Enabled), r => Assert.Equal(memberId, r.TargetId));
        Assert.All(rows, r => Assert.Equal("127.0.0.1", r.RemoteIp));
        Assert.Contains("\"removed\": [\"builder\"]", rows[4].Details);
        foreach (var secret in new[] { token, revoked, IdentityApp.MemberPassword, IdentityApp.OwnerPassword, "skx_inv_" })
            Assert.All(rows, r => Assert.DoesNotContain(secret, r.Details ?? ""));
    }

    /// <summary>
    /// The row commits with the change or not at all: a failure after the row was written (the refresh-chain DELETE,
    /// forced here) rolls back both the role change and its row. Negative check: write the row outside the transaction
    /// and this test fails on the row that stays.
    /// </summary>
    [Fact]
    public async Task AFailedChange_LeavesNoRow()
    {
        var (_, memberId) = await App.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder);
        await ExecuteAsync("""
            CREATE FUNCTION test_refuse() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'refresh store down'; END $$;
            CREATE TRIGGER test_refuse BEFORE DELETE ON identity_refresh_sessions FOR EACH STATEMENT EXECUTE FUNCTION test_refuse();
            """);
        try
        {
            var roles = await Owner().PutAsJsonAsync($"/api/identity/people/{memberId}/roles", new { roles = new[] { SkanyxxRoles.Employee } }, cancellationToken: TestContext.Current.CancellationToken);
            var disable = await Owner().PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, roles.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, disable.StatusCode);
            Assert.Empty(await Postgres.AuditAsync(AuditActions.RolesChanged));
            Assert.Empty(await Postgres.AuditAsync(AuditActions.Disabled));
            var person = (await Owner().GetFromJsonAsync<JsonElement>("/api/identity/people", cancellationToken: TestContext.Current.CancellationToken)).EnumerateArray().Single(p => p.GetProperty("id").GetString() == memberId);
            Assert.Equal(["builder"], person.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            Assert.False(person.GetProperty("disabled").GetBoolean());
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER test_refuse ON identity_refresh_sessions; DROP FUNCTION test_refuse();");
        }
    }

    [Fact]
    public async Task TheAudit_IsTheOwnersOnly_NewestFirst_AndFiltered()
    {
        var (_, memberId) = await App.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);
        await Owner().PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        await Owner().PostAsync($"/api/identity/people/{memberId}/enable", null, TestContext.Current.CancellationToken);

        var anonymous = await App.Client().GetAsync("/api/identity/audit", TestContext.Current.CancellationToken);
        var supervisor = await App.Client(bearer: (await App.SignInBearerAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword)).AccessToken)
            .GetAsync("/api/identity/audit", TestContext.Current.CancellationToken);
        var all = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/audit", cancellationToken: TestContext.Current.CancellationToken);
        var disabled = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/audit?action=person.disabled", cancellationToken: TestContext.Current.CancellationToken);
        var aboutMember = await Owner().GetFromJsonAsync<JsonElement>($"/api/identity/audit?userId={memberId}", cancellationToken: TestContext.Current.CancellationToken);
        var page = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/audit?limit=2", cancellationToken: TestContext.Current.CancellationToken);
        var next = await Owner().GetFromJsonAsync<JsonElement>($"/api/identity/audit?limit=2&before={page[1].GetProperty("id").GetInt64()}", cancellationToken: TestContext.Current.CancellationToken);
        var tooMany = await Owner().GetAsync("/api/identity/audit?limit=201", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, supervisor.StatusCode);
        Assert.Equal(AuditActions.OwnerRouteRefused, all[0].GetProperty("action").GetString()); // the supervisor's refusal, newest first
        Assert.Equal(memberId, all[0].GetProperty("actorId").GetString());
        Assert.Equal("person.disabled", disabled.EnumerateArray().Single().GetProperty("action").GetString());
        Assert.All(aboutMember.EnumerateArray(), e => Assert.True(
            e.GetProperty("actorId").GetString() == memberId || e.GetProperty("targetId").GetString() == memberId));
        Assert.Equal(2, page.GetArrayLength());
        Assert.True(next[0].GetProperty("id").GetInt64() < page[1].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Rows_CannotBeChanged()
    {
        await App.InviteAsync(_owner);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("""UPDATE identity_audit SET "ActorId" = 'someone-else'"""));

        Assert.Contains("append-only", update.MessageText);
    }

    /// <summary>D155: old rows and invites that stopped being pending before the cutoff go; newer ones and pending invites stay; the purge leaves its own row.</summary>
    [Fact]
    public async Task Retention_RemovesOldRowsAndFinishedInvites_AndRecordsIt()
    {
        await App.InviteAsync(_owner, "pending@skanyxx.example");
        await App.AddMemberAsync(_owner);
        await App.InviteAsync(_owner, "revoked@skanyxx.example");
        await using (var db = Postgres.CreateDbContext())
        {
            var old = DateTimeOffset.UtcNow.AddDays(-400);
            // Only the test back-dates rows: with the append-only trigger off for the moment.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE identity_audit DISABLE TRIGGER identity_audit_no_update", cancellationToken: TestContext.Current.CancellationToken);
            await db.Audit.Where(a => a.Action == AuditActions.InviteAccepted).ExecuteUpdateAsync(s => s.SetProperty(a => a.AtUtc, old), cancellationToken: TestContext.Current.CancellationToken);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE identity_audit ENABLE TRIGGER identity_audit_no_update", cancellationToken: TestContext.Current.CancellationToken);
            await db.Invites.Where(i => i.Email == IdentityApp.MemberEmail).ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedUtc, old), cancellationToken: TestContext.Current.CancellationToken);
            await db.Invites.Where(i => i.Email == "revoked@skanyxx.example")
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedUtc, old).SetProperty(i => i.CreatedUtc, old).SetProperty(i => i.ExpiresUtc, old.AddDays(7)), cancellationToken: TestContext.Current.CancellationToken);
        }
        var before = (await Postgres.AuditAsync()).Count;

        var removed = await App.Services.GetRequiredService<IdentityRetention>().RunOnceAsync(CancellationToken.None);

        Assert.Equal((1, 2, 0), removed);
        await using var after = Postgres.CreateDbContext();
        Assert.Equal(["pending@skanyxx.example"], await after.Invites.Select(i => i.Email).ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
        var rows = await Postgres.AuditAsync();
        Assert.Equal(before, rows.Count); // one gone, the purge's own row added
        Assert.DoesNotContain(rows, r => r.Action == AuditActions.InviteAccepted);
        var purge = rows[^1];
        Assert.Equal(AuditActions.RetentionPurged, purge.Action);
        Assert.Contains("\"auditRows\": 1", purge.Details);
        Assert.Contains("\"invites\": 2", purge.Details);
    }

    /// <summary>CR L8 / D169: TRUNCATE skips row triggers, so it has a statement trigger of its own.</summary>
    [Fact]
    public async Task TheTable_CannotBeTruncated()
    {
        await App.InviteAsync(_owner);

        var truncate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("TRUNCATE identity_audit"));

        Assert.Contains("append-only", truncate.MessageText);
        Assert.NotEmpty(await Postgres.AuditAsync());
    }

    /// <summary>CR L8: the Audit page's action filter walks an (Action, Id) index, not the whole table.</summary>
    [Fact]
    public async Task TheActionFilter_HasItsIndex()
    {
        await using var db = Postgres.CreateDbContext();
        var definition = await db.Database.SqlQuery<string>(
            $"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'identity_audit' AND indexname = 'IX_identity_audit_Action_Id'").SingleAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("(\"Action\", \"Id\")", definition);
    }

    /// <summary>CR L9 / D169: the purge deletes in batches until nothing old is left.</summary>
    [Fact]
    public async Task Retention_DeletesInBatches_UntilNothingOldIsLeft()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-400);
        await using (var db = Postgres.CreateDbContext())
            for (var i = 0; i < 5; i++)
                await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO identity_audit ("AtUtc", "Action") VALUES ({old}, 'test.old')""", cancellationToken: TestContext.Current.CancellationToken);
        var retention = App.Services.GetRequiredService<IdentityRetention>();
        retention.BatchSize = 2;

        var removed = await retention.RunOnceAsync(CancellationToken.None);

        Assert.Equal(5, removed.Audit);
        Assert.Empty(await Postgres.AuditAsync("test.old"));
        Assert.Contains("\"auditRows\": 5", Assert.Single(await Postgres.AuditAsync(AuditActions.RetentionPurged)).Details);
    }

    /// <summary>
    /// Verifier G4 / CR L7 / D167: refusals anyone can cause are written once a minute per kind, the next row counting
    /// the ones left out; the Warning line is still there for each.
    /// </summary>
    [Fact]
    public async Task AnonymousRefusals_AreSampled_AndTheSkippedOnesCounted()
    {
        var clock = new ManualClock();
        await using var app = await IdentityApp.StartAsync(Postgres.ConnectionString, services: s => s.AddSingleton<TimeProvider>(clock));

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await app.LookupInviteAsync("skx_inv_guess-" + i)).StatusCode);
        var withinTheMinute = await Postgres.AuditAsync(AuditActions.InviteLookupRefused);
        clock.Advance(AuditSampler.Window);
        await app.LookupInviteAsync("skx_inv_guess-later");
        var rows = await Postgres.AuditAsync(AuditActions.InviteLookupRefused);

        Assert.Single(withinTheMinute);
        Assert.Contains("\"skippedBefore\": 0", withinTheMinute[0].Details);
        Assert.Equal(2, rows.Count);
        Assert.Contains("\"skippedBefore\": 2", rows[1].Details);
    }

    /// <summary>CR L7: an owner-route refusal's row is written even when the caller has gone (no abort thrown out of authorization).</summary>
    [Fact]
    public async Task AnOwnerRouteRefusal_IsRecorded_EvenWhenTheCallerAborted()
    {
        var handler = App.Services.GetServices<IAuthorizationHandler>().OfType<OwnerRouteRefusals>().Single();
        await using var scope = App.Services.CreateAsyncScope();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = aborted.Token };
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "someone")], "test"));
        var context = new AuthorizationHandlerContext([new RolesAuthorizationRequirement([SkanyxxRoles.Owner])], user, http);

        await handler.HandleAsync(context);

        Assert.Equal("someone", Assert.Single(await Postgres.AuditAsync(AuditActions.OwnerRouteRefused)).ActorId);
    }

    /// <summary>QA-2 L4: owner-route refusals are sampled per route and caller, so a second person refused on the same route has their own row.</summary>
    [Fact]
    public async Task OwnerRouteRefusals_AreSampledPerCaller()
    {
        var handler = App.Services.GetServices<IAuthorizationHandler>().OfType<OwnerRouteRefusals>().Single();
        await using var scope = App.Services.CreateAsyncScope();
        foreach (var caller in new[] { "first", "first", "second", "second" })
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, caller)], "test"));
            await handler.HandleAsync(new AuthorizationHandlerContext([new RolesAuthorizationRequirement([SkanyxxRoles.Owner])], user,
                new DefaultHttpContext { RequestServices = scope.ServiceProvider }));
        }

        var rows = await Postgres.AuditAsync(AuditActions.OwnerRouteRefused);

        Assert.Equal(["first", "second"], rows.Select(r => r.ActorId).Order());
    }

    private HttpClient Owner() => App.Client(bearer: _owner);

    private async Task ExecuteAsync(string sql)
    {
        await using var db = Postgres.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
