using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Features.SignIn;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// CR3 m1: a sign-in that finds its account busy is a retryable 429, never "invalid email or password" for a correct
/// password. SEC3 R3-1: the lock is keyed on 64 bits, so an email whose 32-bit hashtext collides does not contend.
/// </summary>
public sealed class SignInBusyTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    // hashtext() of the two normalized emails is equal (-389449364); found by the round-3 security review.
    private const string Victim = "v14222@skanyxx.example";
    private const string Collider = "V122313@SKANYXX.EXAMPLE";
    private const string VictimPassword = "the victim's long password";

    [Fact]
    public async Task BusyAccount_Is429_WithRetryAfter_AndTheBusyMessage()
    {
        await App.BootstrapAsync();

        var response = await WhileLockedAsync("OWNER@SKANYXX.EXAMPLE", () => App.SignInAsync());
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        Assert.Equal(SignInHandler.BusyMessage, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ParallelCorrectSignIns_AreNeverRefusedAsInvalid()
    {
        await App.BootstrapAsync();

        for (var round = 0; round < 10; round++)
        {
            var statuses = (await Task.WhenAll(App.SignInAsync(), App.SignInAsync())).Select(r => r.StatusCode).ToList();

            Assert.Contains(HttpStatusCode.OK, statuses);
            Assert.All(statuses, s => Assert.Contains(s, new[] { HttpStatusCode.OK, HttpStatusCode.TooManyRequests }));
        }
    }

    [Fact]
    public async Task EmailWithACollidingHashtext_DoesNotHoldTheVictimsLock()
    {
        await App.BootstrapAsync();
        await using (var scope = App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.CreateAsync(new IdentityUser { UserName = Victim, Email = Victim }, VictimPassword)).Succeeded);
        }
        await using (var db = Postgres.CreateDbContext())
            Assert.Equal(
                await db.Database.SqlQuery<int>($"SELECT hashtext({Collider}) AS \"Value\"").SingleAsync(),
                await db.Database.SqlQuery<int>($"SELECT hashtext({Victim.ToUpperInvariant()}) AS \"Value\"").SingleAsync());

        var response = await WhileLockedAsync(Collider, () => App.SignInAsync(Victim, VictimPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Holds the account lock for <paramref name="normalizedEmail"/> the way an in-flight sign-in does.</summary>
    private async Task<HttpResponseMessage> WhileLockedAsync(string normalizedEmail, Func<Task<HttpResponseMessage>> action)
    {
        await using var db = Postgres.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.True(await AccountLock.TryAcquireAsync(db, normalizedEmail, CancellationToken.None));
        return await action();
    }
}
