using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// SEC2-N2: the owner holding the bootstrap token signs in past a lockout an attacker keeps renewing. The password is
/// still checked and a wrong one still counts; the token does nothing for anyone but the owner, and a wrong token is
/// just an ordinary sign-in.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BreakGlassSignInTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Token = IdentityApp.BootstrapToken;
    private const string MemberEmail = "member@skanyxx.example";
    private const string MemberPassword = "a colleague's long password";
    private readonly WarningLog _log = new();
    private IdentityApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:BootstrapToken"] = Token,
            services: s => s.AddSingleton<ILoggerProvider>(_log));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync(token: Token)).StatusCode);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Owner_WithTokenAndPassword_SignsIn_WhileAnAttackerKeepsTheAccountLocked()
    {
        using var stop = new CancellationTokenSource();
        var attacker = Task.WhenAll(Enumerable.Range(0, 20).Select(async worker =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
                await _app.SignInAsync(password: $"wrong password {worker}.{i}");
        }));
        await WaitUntilLockedAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await _app.SignInAsync(bootstrapToken: Token)).StatusCode);
        var withoutToken = await _app.SignInAsync();
        await stop.CancelAsync();
        await attacker;

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Contains(withoutToken.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests });
    }

    [Fact]
    public async Task Owner_WithToken_WrongPassword_IsRefused_AndCounted()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await _app.SignInAsync(password: "wrong password " + i, bootstrapToken: Token)).StatusCode);

        var owner = await postgres.OwnerAsync();
        var right = await _app.SignInAsync(bootstrapToken: Token);

        Assert.True(owner.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData(Token + "x")]
    [InlineData("")]
    public async Task WrongToken_IsTheOrdinarySignIn(string token)
    {
        await LockAsync(IdentityApp.OwnerEmail);

        var response = await _app.SignInAsync(bootstrapToken: token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonOwner_WithToken_StaysLockedOut()
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.CreateAsync(new IdentityUser { UserName = MemberEmail, Email = MemberEmail }, MemberPassword)).Succeeded);
        }
        await LockAsync(MemberEmail);

        var response = await _app.SignInAsync(MemberEmail, MemberPassword, bootstrapToken: Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>SEC3 R3-4: every sign-in presenting a token leaves a Warning with the client address, whatever the result.</summary>
    [Fact]
    public async Task EverySignInWithAToken_LogsAWarning_WithTheClientAddress()
    {
        await _app.SignInAsync(bootstrapToken: Token);
        await _app.SignInAsync(password: "wrong password", bootstrapToken: Token);
        await _app.SignInAsync(bootstrapToken: "wrong");
        await _app.SignInAsync();

        var logged = _log.Warnings.Where(w => w.StartsWith("Sign-in with a bootstrap token")).ToList();

        Assert.Equal(3, logged.Count);
        Assert.All(logged, w => Assert.Contains("127.0.0.1", w));
        Assert.Contains(logged, w => w.Contains("token valid, result Ok"));
        Assert.Contains(logged, w => w.Contains("token valid, result Unauthorized"));
        Assert.Contains(logged, w => w.Contains("token wrong, result Ok"));
    }

    private async Task LockAsync(string email)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.True((await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1))).Succeeded);
    }

    private async Task WaitUntilLockedAsync()
    {
        for (var i = 0; i < 200 && !((await postgres.OwnerAsync()).LockoutEnd > DateTimeOffset.UtcNow); i++)
            await Task.Delay(25);
        Assert.True((await postgres.OwnerAsync()).LockoutEnd > DateTimeOffset.UtcNow);
    }
}
