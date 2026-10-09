using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>
/// The owner's Microsoft sign-in settings API: owner only, validated, the client secret write-only (never returned,
/// never logged, protected at rest), audited, and applied to the OIDC options without a restart (A3).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EntraSettingsTests(PostgresFixture postgres) : IAsyncLifetime
{
    public const string Tenant = "11111111-1111-1111-1111-111111111111";
    public const string Client = "22222222-2222-2222-2222-222222222222";
    public const string Secret = "THE-CLIENT-SECRET~value";
    public const string Group = "00000000-0000-0000-0000-0000000000a1";

    private readonly AllLog _log = new();
    private IdentityApp _app = null!;
    private string _owner = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s.AddSingleton<ILoggerProvider>(_log));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
        _owner = (await _app.SignInBearerAsync()).AccessToken;
        await CreateTeamAsync("billing");
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task TheOwner_SavesAndReads_TheSecretIsNeverReturned_NorLogged()
    {
        var saved = await PutAsync(Settings(groups: [Map(Group.ToUpperInvariant(), "Finance", ["supervisor", "employee"], ["billing"])]));
        var read = await Owner().GetAsync("/api/identity/entra/settings", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        foreach (var body in new[] { await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken) })
        {
            Assert.DoesNotContain(Secret, body);
            Assert.DoesNotContain("clientSecret\"", body);
        }
        var json = await read.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(json.GetProperty("enabled").GetBoolean());
        Assert.True(json.GetProperty("clientSecretSet").GetBoolean());
        Assert.True(json.GetProperty("active").GetBoolean());
        Assert.Equal(Tenant, json.GetProperty("tenantId").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("redirectUri").ValueKind); // Development without PublicBaseUrl
        Assert.Equal($$"""[{"groupId":"{{Group}}","label":"Finance","roles":["employee","supervisor"],"teams":["billing"]}]""",
            json.GetProperty("groups").GetRawText());
        Assert.All(_log.Lines, line => Assert.DoesNotContain(Secret, line));
        var audit = Assert.Single(_log.Lines, l => l.Contains("Microsoft sign-in settings saved by"));
        Assert.StartsWith("Warning:", audit);
        Assert.Contains(await OwnerIdAsync(), audit);
        Assert.Contains("secret replaced", audit);
        Assert.Contains($"{Group}=>[employee,supervisor,team:billing]", audit);
    }

    [Fact]
    public async Task TheSecret_IsDataProtected_AtRest_AndAnEmptyOneKeepsIt()
    {
        await PutAsync(Settings());
        var kept = await PutAsync(Settings(secret: null, client: "33333333-3333-3333-3333-333333333333"));

        await using var db = postgres.CreateDbContext();
        var stored = (await db.EntraSettings.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).ProtectedClientSecret!;
        Assert.DoesNotContain(Secret, stored);
        Assert.Equal(Secret, _app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Skanyxx.Identity.Entra.ClientSecret").Unprotect(stored));
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Contains("secret kept", _log.Lines.Last(l => l.Contains("settings saved by")));
        Assert.Equal(Secret, Options().ClientSecret);
    }

    /// <summary>A3: the options are rebuilt from the new values on the next use — authority, client, secret and metadata manager.</summary>
    [Fact]
    public async Task ASave_RebuildsTheOidcOptions_WithoutARestart()
    {
        var off = Options();
        await PutAsync(Settings());
        var first = Options();
        await PutAsync(Settings(tenant: "44444444-4444-4444-4444-444444444444", client: "55555555-5555-5555-5555-555555555555", secret: "second-secret"));
        var second = Options();
        await PutAsync(Settings(enabled: false, secret: null));
        var disabled = Options();

        Assert.Equal("microsoft-sign-in-is-off", off.ClientId);
        Assert.Equal($"https://login.microsoftonline.com/{Tenant}/v2.0", first.Authority);
        Assert.Equal(Client, first.ClientId);
        Assert.Equal("https://login.microsoftonline.com/44444444-4444-4444-4444-444444444444/v2.0", second.Authority);
        Assert.Equal("55555555-5555-5555-5555-555555555555", second.ClientId);
        Assert.Equal("second-secret", second.ClientSecret);
        Assert.NotSame(first.ConfigurationManager, second.ConfigurationManager);
        Assert.Equal("microsoft-sign-in-is-off", disabled.ClientId);
        Assert.NotNull(disabled.Configuration);
        Assert.True(first.UsePkce);
        Assert.Equal(PushedAuthorizationBehavior.Disable, first.PushedAuthorizationBehavior);
        Assert.False(first.MapInboundClaims);
        Assert.Equal("code", first.ResponseType);
        Assert.Equal(["openid", "profile", "email"], first.Scope);
    }

    /// <summary>
    /// CR m3: the backchannel client lives in the cached options until the next save, so its connections are recycled
    /// by lifetime instead (DNS changes at Microsoft reach it).
    /// </summary>
    [Fact]
    public void TheOidcBackchannel_RecyclesItsConnections()
    {
        HttpMessageHandler handler = _app.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(EntraOidcOptions.Backchannel);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        Assert.Equal(TimeSpan.FromMinutes(5), Assert.IsType<SocketsHttpHandler>(handler).PooledConnectionLifetime);
    }

    public static TheoryData<string, object> Invalid => new()
    {
        { "Tenant", Settings(tenant: "contoso.onmicrosoft.com") },
        { "Client", Settings(client: "my-app") },
        { "group id", Settings(groups: [Map("Finance", null, ["employee"], [])]) },
        { "owner role cannot be granted", Settings(groups: [Map(Group, null, ["owner"], [])]) },
        { "must give a role or a team", Settings(groups: [Map(Group, null, [], [])]) },
        { "mapped once", Settings(groups: [Map(Group, null, ["employee"], []), Map(Group.ToUpperInvariant(), null, ["builder"], [])]) },
        { "Map at least one group", Settings(groups: []) },
        { "team", Settings(groups: [Map(Group, null, [], ["Not A Slug"])]) },
        { "invisible", Settings(groups: [Map(Group, "Fin‮nce", ["employee"], [])]) },
        { "control", Settings(secret: "a\u0000b") }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task InvalidSettings_Are400_AndNothingIsSaved(string mentions, object settings)
    {
        var response = await PutAsync(settings);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(mentions, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await using var db = postgres.CreateDbContext();
        Assert.False(await db.EntraSettings.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TurningItOn_WithNoSecretStored_Is400()
    {
        var response = await PutAsync(Settings(secret: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Enter the client secret", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnknownTeam_Is404()
    {
        var response = await PutAsync(Settings(groups: [Map(Group, null, [], ["nope"])]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("No team 'nope'", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlyTheOwner_ReadsOrChangesTheSettings()
    {
        var (member, _) = await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);

        var get = await _app.Client(bearer: member.AccessToken).GetAsync("/api/identity/entra/settings", TestContext.Current.CancellationToken);
        var put = await _app.Client(bearer: member.AccessToken).PutAsJsonAsync("/api/identity/entra/settings", Settings(), cancellationToken: TestContext.Current.CancellationToken);
        var anonymous = await _app.Client().GetAsync("/api/identity/entra/settings", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains(_log.Lines, l => l.StartsWith("Warning: Owner-only route PUT api/identity/entra/settings refused"));
        await using var db = postgres.CreateDbContext();
        Assert.False(await db.EntraSettings.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Outside Development the redirect URI is built on Identity:PublicBaseUrl only; turning sign-in on without it is 409.</summary>
    [Fact]
    public async Task OutsideDevelopment_TurningItOn_NeedsPublicBaseUrl()
    {
        await using var production = await IdentityApp.StartAsync(postgres.ConnectionString, s =>
        {
            s["Identity:BootstrapToken"] = IdentityApp.BootstrapToken;
            s.Remove("Identity:PublicBaseUrl");
        }, environment: "Production");
        var owner = (await production.SignInBearerAsync()).AccessToken;

        var on = await production.Client(bearer: owner).PutAsJsonAsync("/api/identity/entra/settings", Settings(), cancellationToken: TestContext.Current.CancellationToken);
        var off = await production.Client(bearer: owner).PutAsJsonAsync("/api/identity/entra/settings", Settings(enabled: false), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, on.StatusCode);
        Assert.Contains("Identity:PublicBaseUrl", await on.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
    }

    /// <summary>
    /// CR M1: saves racing from an empty table (the first-save insert included) all succeed, one at a time, each with its
    /// own version; this instance and a replica (its periodic check, run once here) both end on the last one.
    /// </summary>
    [Fact]
    public async Task ConcurrentSaves_AllSucceed_EachGetsItsOwnVersion_AndEveryReplicaEndsOnTheLast()
    {
        await using var replica = await IdentityApp.StartAsync(postgres.ConnectionString);
        var clients = Enumerable.Range(1, 8).Select(i => $"55555555-5555-5555-5555-{i:D12}").ToList();

        var saves = await Task.WhenAll(clients.Select(c => PutAsync(Settings(client: c))));
        await PollAsync(replica);

        Assert.All(saves, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var db = postgres.CreateDbContext();
        var stored = await db.EntraSettings.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(clients.Count, stored.Version);
        Assert.Single(await db.EntraGroups.ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
        foreach (var app in new[] { _app, replica })
        {
            var current = app.Services.GetRequiredService<EntraSettingsCache>().Current;
            Assert.Equal(stored.Version, current.Version);
            Assert.Equal(stored.ClientId, current.ClientId);
            Assert.Equal(stored.ClientId, app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("entra").ClientId);
        }
    }

    /// <summary>A replica started before a save picks it up at its next check, options included (no restart, A3 across replicas).</summary>
    [Fact]
    public async Task AReplica_PicksUpASaveMadeElsewhere_AtItsNextCheck()
    {
        await using var replica = await IdentityApp.StartAsync(postgres.ConnectionString);
        var before = replica.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("entra").ClientId;

        await PutAsync(Settings());
        await PollAsync(replica);

        Assert.Equal("microsoft-sign-in-is-off", before);
        Assert.Equal(Client, replica.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("entra").ClientId);
        Assert.True(replica.Services.GetRequiredService<EntraSettingsCache>().Current.CanSignIn);
    }

    /// <summary>SEC L1: outside Development without a Data Protection certificate, storing a secret is warned about (page/API flag and a Warning), not refused.</summary>
    [Fact]
    public async Task StoringASecret_WithUnencryptedKeys_IsWarnedAbout_NotRefused()
    {
        var log = new AllLog();
        await using var production = await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:BootstrapToken"] = IdentityApp.BootstrapToken,
            environment: "Production", services: s => s.AddSingleton<ILoggerProvider>(log));
        var owner = (await production.SignInBearerAsync()).AccessToken;

        var saved = await production.Client(bearer: owner).PutAsJsonAsync("/api/identity/entra/settings", Settings(), cancellationToken: TestContext.Current.CancellationToken);
        var development = await Owner().GetFromJsonAsync<JsonElement>("/api/identity/entra/settings", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.True((await saved.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("secretKeysUnencrypted").GetBoolean());
        Assert.StartsWith("Warning:", Assert.Single(log.Lines, l => l.Contains("client secret stored by") && l.Contains("unencrypted")));
        Assert.All(log.Lines, line => Assert.DoesNotContain(Secret, line));
        Assert.False(development.GetProperty("secretKeysUnencrypted").GetBoolean()); // Development: no warning
    }

    public static object Settings(
        bool enabled = true, string tenant = Tenant, string client = Client, string? secret = Secret, object[]? groups = null) =>
        new { enabled, tenantId = tenant, clientId = client, clientSecret = secret, groups = groups ?? [Map(Group, null, ["employee"], [])] };

    public static object Map(string groupId, string? label, string[] roles, string[] teams) => new { groupId, label, roles, teams };

    private HttpClient Owner() => _app.Client(bearer: _owner);

    /// <summary>What <see cref="EntraSettingsRefresher"/> does on each tick, run once now.</summary>
    private static async Task PollAsync(IdentityApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await app.Services.GetRequiredService<EntraSettingsCache>().RefreshAsync(scope.ServiceProvider.GetRequiredService<AccountsDbContext>(), CancellationToken.None);
    }

    private Task<HttpResponseMessage> PutAsync(object settings) => Owner().PutAsJsonAsync("/api/identity/entra/settings", settings);

    private OpenIdConnectOptions Options() => _app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("entra");

    private async Task<string> OwnerIdAsync() =>
        (await Owner().GetFromJsonAsync<JsonElement>("/api/identity/me")).GetProperty("id").GetString()!;

    private async Task CreateTeamAsync(string slug)
    {
        Assert.Equal(HttpStatusCode.Created, (await Owner().PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Owner().PostAsJsonAsync("/api/identity/org/teams", new { slug, name = "Billing", department = "finance" })).StatusCode);
    }
}
