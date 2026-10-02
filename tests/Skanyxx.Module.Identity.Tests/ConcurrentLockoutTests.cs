using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC S1: parallel wrong passwords never get more than the lockout budget of real checks, and every check that runs
/// is counted. A guess that finds the account busy is refused without a check (SEC2-N1), so bursts repeat until the
/// budget is spent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConcurrentLockoutTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ParallelWrongPasswords_CheckAtMostTheLimit_CountEveryCheck_AndLockTheAccount()
    {
        await postgres.ResetAsync();
        var hasher = new CountingHasher();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString,
            services: s => s.AddSingleton<IPasswordHasher<IdentityUser>>(hasher));
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        hasher.OwnerChecks = 0;

        var first = await BurstAsync(app, 0);
        var countedAfterFirst = (await postgres.OwnerAsync()).AccessFailedCount;
        var checksAfterFirst = hasher.OwnerChecks;
        var responses = first.ToList();
        for (var burst = 1; burst < 50 && hasher.OwnerChecks < 5; burst++)
            responses.AddRange(await BurstAsync(app, burst));
        responses.AddRange(await BurstAsync(app, 50)); // after the lock: no more real checks

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
        if (checksAfterFirst < 5)
            Assert.Equal(checksAfterFirst, countedAfterFirst); // no lost update: every check that ran counted
        Assert.Equal(5, hasher.OwnerChecks);
        Assert.True((await postgres.OwnerAsync()).LockoutEnd > DateTimeOffset.UtcNow);
    }

    private static Task<HttpResponseMessage[]> BurstAsync(IdentityApp app, int burst) =>
        Task.WhenAll(Enumerable.Range(0, 24).Select(i => app.SignInAsync(password: $"wrong password {burst}.{i}")));
}
