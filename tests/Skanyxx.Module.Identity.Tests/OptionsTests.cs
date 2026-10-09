using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Tests;

[Collection(PostgresCollection.Name)]
public sealed class OptionsTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Identity:PasswordMinLength", "8")]
    [InlineData("Identity:LockoutMaxFailedAttempts", "0")]
    [InlineData("Identity:LockoutMinutes", "0")]
    [InlineData("Identity:SessionDays", "0")]
    [InlineData("Identity:BootstrapToken", "shorter-than-thirty-two-chars")]
    [InlineData("Identity:BootstrapToken", "                                        ")]       // whitespace only
    [InlineData("Identity:BootstrapToken", " a-bootstrap-token-of-at-least-32-characters")]  // leading space
    [InlineData("Identity:BootstrapToken", "a-bootstrap-token-of-at-least-32-characters\n")] // trailing newline
    public async Task WeakSettings_StopStartup(string key, string value)
    {
        await using var app = IdentityApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = postgres.ConnectionString,
            [key] = value
        });

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// SEC M1 / SEC2 N3: a set Identity:PublicBaseUrl is used exactly as written, so anything Uri would quietly trim or
    /// rewrite, and anything that is not https to a real host, stops startup.
    /// </summary>
    [Theory]
    [InlineData("Production", "http://skanyxx.example")]
    [InlineData("Production", "http://10.0.0.5:5282")]
    [InlineData("Production", "http://localhost.evil.example")]
    [InlineData("Production", "skanyxx.example")]
    [InlineData("Production", "/skanyxx")]
    [InlineData("Production", "https://skanyxx.example/?tenant=a")]
    [InlineData("Production", "https://skanyxx.example/#x")]
    [InlineData("Production", "https://user:pass@skanyxx.example")]
    [InlineData("Production", " https://skanyxx.example")]
    [InlineData("Production", "https://skanyxx.example ")]
    [InlineData("Production", "https://skanyxx.example/a b")]
    [InlineData("Production", "http:\\\\localhost:5282")]
    [InlineData("Production", "https://skanyxx.example\\base")]
    [InlineData("Production", "   ")]
    [InlineData("Development", "ftp://skanyxx.example")]
    [InlineData("Development", "not a url")]
    public async Task BadPublicBaseUrl_StopsStartup(string environment, string publicBaseUrl)
    {
        await using var app = IdentityApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = postgres.ConnectionString,
            ["Identity:BootstrapToken"] = IdentityApp.BootstrapToken,
            ["Identity:PublicBaseUrl"] = publicBaseUrl
        }, environment);

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Identity:PublicBaseUrl", error.Message);
    }

    /// <summary>Unset no longer stops startup anywhere (upgraded installs have no such key); invites wait for it instead.</summary>
    [Theory]
    [InlineData("Production", "https://skanyxx.example")]
    [InlineData("Production", "http://localhost:5282")]   // the desktop installs: the link never leaves the machine
    [InlineData("Production", "http://127.0.0.1:5282")]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Development", null)]
    [InlineData("Development", "http://localhost:5283")]
    public async Task GoodPublicBaseUrl_Starts(string environment, string? publicBaseUrl)
    {
        await using var app = IdentityApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = postgres.ConnectionString,
            ["Identity:BootstrapToken"] = IdentityApp.BootstrapToken,
            ["Identity:PublicBaseUrl"] = publicBaseUrl
        }, environment);

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MissingConnectionString_StopsStartup()
    {
        await using var app = IdentityApp.Build(new Dictionary<string, string?>());

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }
}
