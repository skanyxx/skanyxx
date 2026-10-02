using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>CR M1: a concurrent write to the user row must not let sign-out answer 204 without rotating the stamp.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SignOutRaceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly RaceSchedule _races = new();
    private IdentityApp _app = null!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s =>
        {
            s.AddSingleton(_races);
            s.AddScoped<UserManager<IdentityUser>, RacingUserManager>();
        });
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task OneLostRace_IsRetried_AndTheStampRotates()
    {
        var tokens = await _app.SignInBearerAsync();
        var before = (await postgres.OwnerAsync()).SecurityStamp;
        _races.Remaining = 1;

        var signOut = await _app.Client(bearer: tokens.AccessToken).PostAsync("/api/identity/sign-out", null);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.NotEqual(before, (await postgres.OwnerAsync()).SecurityStamp);
        Assert.Equal(0, _races.Remaining);
    }

    [Fact]
    public async Task TwoLostRaces_FailLoudly_InsteadOf204()
    {
        var tokens = await _app.SignInBearerAsync();
        _races.Remaining = 2;

        var signOut = await _app.Client(bearer: tokens.AccessToken).PostAsync("/api/identity/sign-out", null);

        Assert.Equal(HttpStatusCode.InternalServerError, signOut.StatusCode);
    }

    public sealed class RaceSchedule
    {
        public int Remaining;
    }

    /// <summary>Before each scheduled stamp update, another writer changes the row first (as a failed sign-in would).</summary>
    private sealed class RacingUserManager(
        AccountsDbContext db, RaceSchedule races,
        IUserStore<IdentityUser> store, IOptions<IdentityOptions> options, IPasswordHasher<IdentityUser> hasher,
        IEnumerable<IUserValidator<IdentityUser>> userValidators, IEnumerable<IPasswordValidator<IdentityUser>> passwordValidators,
        ILookupNormalizer normalizer, IdentityErrorDescriber errors, IServiceProvider services, ILogger<UserManager<IdentityUser>> logger)
        : UserManager<IdentityUser>(store, options, hasher, userValidators, passwordValidators, normalizer, errors, services, logger)
    {
        public override async Task<IdentityResult> UpdateSecurityStampAsync(IdentityUser user)
        {
            if (Interlocked.Decrement(ref races.Remaining) >= 0)
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE identity_users SET \"ConcurrencyStamp\" = {Guid.NewGuid().ToString()} WHERE \"Id\" = {user.Id}");
            else
                Interlocked.Increment(ref races.Remaining);
            return await base.UpdateSecurityStampAsync(user);
        }
    }
}
