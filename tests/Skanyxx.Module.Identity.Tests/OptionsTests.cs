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
    [InlineData("Identity:SecurityStampValidationSeconds", "-1")]
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

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());
    }

    [Fact]
    public async Task MissingConnectionString_StopsStartup()
    {
        await using var app = IdentityApp.Build(new Dictionary<string, string?>());

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());
    }
}
