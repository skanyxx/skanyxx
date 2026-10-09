using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC M2 / CR2-M1 / SEC2 N2: saving roles without supervisor (unchanged ones too) and every disable are announced
/// (<see cref="PrivilegesRevoked"/>) after the commit, so modules can revoke what the person issued, and re-saving is
/// the retry when a handler failed. Roles that keep supervisor, and enabling, announce nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PrivilegesRevokedTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Recorder _recorder = new();
    private readonly WarningLog _log = new();
    private IdentityApp _app = null!;
    private string _owner = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString,
            services: s => s.AddSingleton<INotificationHandler<PrivilegesRevoked>>(_recorder).AddSingleton<ILoggerProvider>(_log));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
        _owner = (await _app.SignInBearerAsync()).AccessToken;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task RolesSavedWithoutSupervisor_AreAnnounced_EvenUnchanged_RolesWithSupervisorAreNot()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);

        await SetRolesAsync(memberId, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder);
        await SetRolesAsync(memberId, SkanyxxRoles.Supervisor);
        Assert.Empty(_recorder.Received);
        await SetRolesAsync(memberId, SkanyxxRoles.Employee);
        await SetRolesAsync(memberId, SkanyxxRoles.Employee);

        var announced = new PrivilegesRevoked(memberId, "roles saved without supervisor");
        Assert.Equal([announced, announced], _recorder.Received);
    }

    [Fact]
    public async Task Disabling_IsAnnounced_EveryTime_EnablingIsNot()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner);

        var disable = await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        var again = await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        var afterDisable = _recorder.Received;
        var enable = await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/enable", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        // D154: a disable says so, so modules that run things for the person stop them.
        var announced = new PrivilegesRevoked(memberId, "account disabled", AccountDisabled: true, AccessRemoved: true);
        Assert.Equal([announced, announced], afterDisable);
        Assert.Equal(afterDisable, _recorder.Received);
    }

    /// <summary>
    /// A failing handler (a memory database outage): the change is saved, the owner gets a 500, an Error names who and
    /// what, and saving the same change again publishes again.
    /// </summary>
    [Theory]
    [InlineData("roles")]
    [InlineData("disable")]
    public async Task AFailingHandler_Is500_LoggedAtError_AndRetryingTheSameChangePublishesAgain(string change)
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);
        _recorder.Throw = true;

        var failed = await ChangeAsync(memberId, change);
        var saved = await _app.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/people", cancellationToken: TestContext.Current.CancellationToken);
        var owedAfterFailure = await OwedAsync(memberId);
        _recorder.Throw = false;
        var retried = await ChangeAsync(memberId, change);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var person = saved.EnumerateArray().Single(p => p.GetProperty("id").GetString() == memberId);
        Assert.Equal(change == "disable", person.GetProperty("disabled").GetBoolean());
        Assert.Equal(change == "roles" ? ["employee"] : ["supervisor"], person.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        var error = Assert.Single(_log.Errors, e => e.StartsWith("Revoking what"));
        Assert.Contains(memberId, error);
        Assert.Contains($"failed after {await OwnerIdAsync()}'s change was saved", error);
        Assert.True(owedAfterFailure); // D13: marked in the change's own transaction
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.False(await OwedAsync(memberId));
        Assert.Equal(2, _recorder.Received.Count);
        Assert.All(_recorder.Received, r => Assert.Equal(memberId, r.UserId));
    }

    /// <summary>
    /// D153: every handler runs even when another one fails first (an unreachable AX must not keep memory from revoking
    /// secrets); the failure still reaches the owner as a 500 and the revocation stays owed. Negative check: with MediatR's
    /// default publisher the second handler never sees the notification.
    /// </summary>
    [Fact]
    public async Task AFailingHandler_DoesNotStopTheOthers()
    {
        var failing = new Recorder { Throw = true };
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s
            .AddSingleton<INotificationHandler<PrivilegesRevoked>>(failing)
            .AddSingleton<INotificationHandler<PrivilegesRevoked>>(_recorder));
        var owner = (await app.SignInBearerAsync()).AccessToken;
        var (_, memberId) = await app.AddMemberAsync(owner, "second@skanyxx.example", SkanyxxRoles.Supervisor);

        var disable = await app.Client(bearer: owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, disable.StatusCode);
        Assert.Single(failing.Received);
        Assert.Equal([new PrivilegesRevoked(memberId, "account disabled", AccountDisabled: true, AccessRemoved: true)], _recorder.Received);
        Assert.True(await OwedAsync(memberId));
    }

    /// <summary>
    /// D154: a retried revocation reads the account as it is now: published by an enable (owed from a failed disable),
    /// it no longer says disabled; published by a role save while disabled, it does.
    /// </summary>
    [Fact]
    public async Task AccountDisabled_IsReadWhenPublishing()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);
        _recorder.Throw = true;
        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        _recorder.Throw = false;

        var enable = await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/enable", null, TestContext.Current.CancellationToken); // retries the owed one
        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        await SetRolesAsync(memberId, SkanyxxRoles.Employee);

        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        Assert.Equal([true, false, true, true], _recorder.Received.Select(r => r.AccountDisabled));
        Assert.Equal(PrivilegeRevocation.Retried, _recorder.Received[1].Reason);
    }

    /// <summary>
    /// D13: a failed revocation stays owed and the next save without supervisor publishes it (the Theory above). D17: a
    /// save that gives supervisor back drops the debt instead, unpublished — what the person issues from then on is
    /// legitimately theirs, and a stale mark would later revoke it — and later saves publish nothing.
    /// </summary>
    [Fact]
    public async Task AnOwedRevocation_IsDroppedBySavingSupervisorBack()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);
        _recorder.Throw = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await ChangeAsync(memberId, "roles")).StatusCode);
        _recorder.Throw = false;

        await SetRolesAsync(memberId, SkanyxxRoles.Supervisor);
        var owed = await OwedAsync(memberId);
        await SetRolesAsync(memberId, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder);

        Assert.False(owed);
        Assert.Equal([new PrivilegesRevoked(memberId, "roles saved without supervisor")], _recorder.Received);
    }

    private async Task<bool> OwedAsync(string userId)
    {
        await using var db = postgres.CreateDbContext();
        return db.PendingRevocations.Any(p => p.UserId == userId);
    }

    private Task<HttpResponseMessage> ChangeAsync(string userId, string change) => change == "roles"
        ? _app.Client(bearer: _owner).PutAsJsonAsync($"/api/identity/people/{userId}/roles", new { roles = new[] { SkanyxxRoles.Employee } })
        : _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{userId}/disable", null);

    private async Task<string> OwnerIdAsync() =>
        (await _app.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/me")).GetProperty("id").GetString()!;

    private async Task SetRolesAsync(string userId, params string[] roles) =>
        Assert.Equal(HttpStatusCode.OK, (await _app.Client(bearer: _owner).PutAsJsonAsync($"/api/identity/people/{userId}/roles", new { roles })).StatusCode);

    /// <summary>Records every notification it gets, including the ones it then fails.</summary>
    private sealed class Recorder : INotificationHandler<PrivilegesRevoked>
    {
        private readonly ConcurrentQueue<PrivilegesRevoked> _received = new();

        public volatile bool Throw;

        public IReadOnlyList<PrivilegesRevoked> Received => [.. _received];

        public Task Handle(PrivilegesRevoked notification, CancellationToken ct)
        {
            _received.Enqueue(notification);
            return Throw ? throw new InvalidOperationException("The memory database is unavailable.") : Task.CompletedTask;
        }
    }
}
