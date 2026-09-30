using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// Verifier round 2 / SEC2 N3: an install without Identity:PublicBaseUrl (every upgraded one) starts and says so;
/// outside Development invites are refused until it is set, before anything is written.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PublicBaseUrlTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Production", null, null, true, false)]
    [InlineData("Production", "https://skanyxx.example", null, false, false)]
    [InlineData("Production", "http://localhost:5282", "http://localhost:5282", false, false)]
    [InlineData("Production", "http://localhost:5282", "http://0.0.0.0:5282", false, true)]
    [InlineData("Production", "http://127.0.0.1:5282", "http://*:5282", false, true)]
    [InlineData("Production", "http://localhost:5282", "http://[::1]:5282", false, false)]
    [InlineData("Production", "https://skanyxx.example", "http://0.0.0.0:5282", false, false)]
    [InlineData("Development", null, null, false, false)]
    [InlineData("Development", "http://localhost:5283", "http://0.0.0.0:5283", false, false)]
    public async Task Startup_WarnsWhenUnset_OrLoopbackWhileListeningBeyondIt(
        string environment, string? publicBaseUrl, string? kestrelUrl, bool unsetWarned, bool loopbackWarned)
    {
        var logs = new WarningLog();
        await using var app = IdentityApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = postgres.ConnectionString,
            ["Identity:BootstrapToken"] = IdentityApp.BootstrapToken,
            ["Identity:PublicBaseUrl"] = publicBaseUrl,
            // Read, never bound: the app is not started here.
            ["Kestrel:Endpoints:Public:Url"] = kestrelUrl
        }, environment, s => s.AddSingleton<ILoggerProvider>(logs));

        await new IdentityModule().InitializeAsync(app.Services);

        Assert.Equal(unsetWarned, logs.Warnings.Any(w => w.Contains("Identity:PublicBaseUrl is not set; invites cannot be created")));
        Assert.Equal(loopbackWarned, logs.Warnings.Any(w => w.Contains("a loopback address, but Skanyxx listens beyond this machine")));
    }

    [Fact]
    public async Task Invite_OutsideDevelopmentWithoutIt_Is409_WritesNothing_AndKeepsTheOlderInvite()
    {
        var connectionString = await postgres.NewDatabaseAsync();
        await using (var configured = await StartAsync(connectionString, IdentityApp.PublicBaseUrl))
        {
            Assert.Equal(HttpStatusCode.Created, (await configured.BootstrapAsync(token: IdentityApp.BootstrapToken)).StatusCode);
            await configured.InviteAsync((await configured.SignInBearerAsync()).AccessToken);
        }

        await using var app = await StartAsync(connectionString, null);
        var owner = (await app.SignInBearerAsync()).AccessToken;
        var refused = await app.CreateInviteAsync(owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder);
        var other = await app.CreateInviteAsync(owner, "someone@skanyxx.example", SkanyxxRoles.Builder);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Contains("Identity:PublicBaseUrl", body.GetProperty("message").GetString());
        Assert.Contains("http://localhost:5282", body.GetProperty("message").GetString());
        await using var db = postgres.CreateDbContext(connectionString);
        var invite = await db.Invites.AsNoTracking().SingleAsync();
        Assert.Equal(IdentityApp.MemberEmail, invite.Email);
        Assert.Null(invite.RevokedUtc);
    }

    private static Task<IdentityApp> StartAsync(string connectionString, string? publicBaseUrl) =>
        IdentityApp.StartAsync(connectionString, s =>
        {
            s["Identity:BootstrapToken"] = IdentityApp.BootstrapToken;
            s["Identity:PublicBaseUrl"] = publicBaseUrl;
        }, environment: "Production");
}
