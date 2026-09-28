using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC2-N1 / CR2-m2: guesses on one email do not queue for its lock. A guess that finds the account busy gets a
/// 429 at once (CR3 m1), so a flood cannot pin the identity pool and stall everyone else's identity calls.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LockContentionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Flood_OnOneEmail_LeavesOtherCallsFast_AndStillLocksTheAccount()
    {
        await postgres.ResetAsync();
        var hasher = new CountingHasher();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:MaxPoolSize"] = "3",
            services: s => s.AddSingleton<IPasswordHasher<IdentityUser>>(hasher));
        Assert.Equal(HttpStatusCode.Created, (await app.BootstrapAsync()).StatusCode);
        hasher.OwnerChecks = 0;
        var responses = new ConcurrentBag<HttpStatusCode>();
        var measured = false;

        // 150 guessers, each retrying until the account is locked and the probe below has been timed.
        var flood = Task.WhenAll(Enumerable.Range(0, 150).Select(async worker =>
        {
            for (var i = 0; i < 40 && !(Volatile.Read(ref measured) && hasher.OwnerChecks >= 5); i++)
                responses.Add((await app.SignInAsync(password: $"wrong password {worker}.{i}")).StatusCode);
        }));
        await Task.Delay(150);
        var timer = Stopwatch.StartNew();
        var status = await app.Client().GetAsync("/api/identity/status");
        timer.Stop();
        Volatile.Write(ref measured, true);
        await flood;

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"/status took {timer.ElapsedMilliseconds} ms during the flood");
        Assert.All(responses, r => Assert.Contains(r, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
        Assert.Equal(5, hasher.OwnerChecks);
        Assert.True((await postgres.OwnerAsync()).LockoutEnd > DateTimeOffset.UtcNow);
    }
}
