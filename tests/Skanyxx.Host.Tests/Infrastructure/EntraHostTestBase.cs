using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// The real Host (Program.cs, every module, the pages) on its own database, with Microsoft Entra ID played by
/// <see cref="MockIdentityProvider"/>. Test configuration only: Microsoft's authority is rewritten to the mock's issuer
/// for the same tenant and plain-http metadata is allowed; Graph and Microsoft's token endpoint are
/// <see cref="FakeGraphHandler"/>; <see cref="PrivilegesRevoked"/> is recorded. Response mode, PKCE, the redirect URI
/// (built on Identity:PublicBaseUrl) and everything else are the production settings.
/// </summary>
[Collection(HostCollection.Name)]
public abstract class EntraHostTestBase(PostgresFixture fixture, MockIdentityProvider idp) : IClassFixture<MockIdentityProvider>, IAsyncLifetime
{
    public const string Tenant = "11111111-1111-1111-1111-111111111111";
    public const string Client = "22222222-2222-2222-2222-222222222222";
    public const string Secret = "THE-CLIENT-SECRET~value";
    public const string Supervisors = "00000000-0000-0000-0000-0000000000a1";
    public const string Billing = "00000000-0000-0000-0000-0000000000b1";
    public const string Unmapped = "00000000-0000-0000-0000-0000000000ff";

    private const string MicrosoftLogin = "https://login.microsoftonline.com/";

    protected HostApp Host { get; private set; } = null!;
    protected Browser OwnerBrowser { get; private set; } = null!;
    protected HttpClient Owner { get; private set; } = null!;
    protected EntraFlow Flow { get; } = new(idp);
    protected FakeGraphHandler Graph { get; } = new();
    protected ConcurrentQueue<PrivilegesRevoked> Revoked { get; } = new();

    /// <summary>While true, the <see cref="PrivilegesRevoked"/> handler records and then throws (a memory database outage).</summary>
    protected bool FailRevocations { get; set; }

    /// <summary>Every request the OIDC handler's backchannel made (metadata, keys, code redemption).</summary>
    protected ConcurrentQueue<Uri> Backchannel { get; } = new();
    protected string ConnectionString { get; private set; } = null!;

    /// <summary>
    /// Points the configured tenant's authority at another mock issuer (read whenever the options are rebuilt). The mock
    /// stamps <c>tid</c> = its issuer id, so this is how a token whose issuer the handler accepts can carry a foreign tid.
    /// </summary>
    protected string? IssuerOverride { get; set; }

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await fixture.NewDatabaseAsync();
        Host = await HostApp.StartAsync(ConnectionString, HostApp.WithoutSandboxes, services: services =>
        {
            services.Configure<OpenIdConnectOptions>("entra", o =>
            {
                o.RequireHttpsMetadata = false;
                if (o.Authority?.StartsWith(MicrosoftLogin) == true)
                    o.Authority = $"{idp.BaseUrl}/{IssuerOverride ?? o.Authority[MicrosoftLogin.Length..].Split('/')[0]}";
            });
            services.AddHttpClient("IGraphMembership").ConfigurePrimaryHttpMessageHandler(() => Graph);
            services.AddHttpClient("entra-oidc").AddHttpMessageHandler(() => new Watching(Backchannel));
            services.AddSingleton<INotificationHandler<PrivilegesRevoked>>(new Recorder(Revoked, () => FailRevocations));
        });
        OwnerBrowser = new Browser(Host);
        var setup = await OwnerBrowser.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        Owner = await Host.OwnerAsync();
        await PostAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" });
        await PostAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" });
        await PostAsync("/api/identity/org/teams", new { slug = "platform", name = "Platform", department = "finance" });
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    protected static object Map(string groupId, string[] roles, string[]? teams = null) => new { groupId, roles, teams = teams ?? [] };

    /// <summary>Supervisors → supervisor; Billing → employee + team billing.</summary>
    protected static object[] DefaultGroups => [Map(Supervisors, ["supervisor"]), Map(Billing, ["employee"], ["billing"])];

    protected async Task SaveSettingsAsync(object[]? groups = null, bool enabled = true, string tenant = Tenant, string client = Client, string? secret = Secret)
    {
        var response = await Owner.PutAsJsonAsync("/api/identity/entra/settings",
            new { enabled, tenantId = tenant, clientId = client, clientSecret = secret, groups = groups ?? DefaultGroups });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    protected static string Oid(int n) => $"aaaaaaaa-0000-0000-0000-{n:D12}";

    protected async Task<JsonElement> PersonAsync(string email) =>
        (await Owner.GetFromJsonAsync<JsonElement>("/api/identity/people"))!.EnumerateArray().Single(p => p.GetProperty("email").GetString() == email);

    protected async Task<int> PeopleCountAsync() => (await Owner.GetFromJsonAsync<JsonElement>("/api/identity/people")).GetArrayLength();

    protected async Task<string[]> TeamsOfAsync(string userId) =>
        [.. (await Owner.GetFromJsonAsync<JsonElement>($"/api/identity/people/{userId}/teams")).EnumerateArray().Select(t => t.GetProperty("slug").GetString()!).Order()];

    /// <summary>The signed-in person behind a browser's cookie: 200 with id and roles, or 401.</summary>
    protected static async Task<(HttpStatusCode Status, string? Id, string[] Roles)> MeAsync(Browser browser)
    {
        var response = await browser.GetAsync("/api/identity/me");
        if (response.StatusCode != HttpStatusCode.OK)
            return (response.StatusCode, null, []);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, me.GetProperty("id").GetString(), [.. me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!)]);
    }

    /// <summary>Writes a Microsoft login straight into the database, around every rule the app applies to linking.</summary>
    protected async Task InsertLoginAsync(string userId, string key)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """INSERT INTO identity_user_logins ("LoginProvider", "ProviderKey", "ProviderDisplayName", "UserId") VALUES ('entra', @key, 'Microsoft', @id)""", connection);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("id", userId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The logins stored for an account, straight from the database.</summary>
    protected async Task<List<(string Provider, string Key)>> LoginsAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "LoginProvider", "ProviderKey" FROM identity_user_logins WHERE "UserId" = @id""", connection);
        command.Parameters.AddWithValue("id", userId);
        var logins = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            logins.Add((reader.GetString(0), reader.GetString(1)));
        return logins;
    }

    /// <summary>Starts linking Microsoft from the Account page, re-entering the password (D10): the challenge's answer.</summary>
    protected static Task<HttpResponseMessage> StartLinkAsync(Browser browser, string password = MemberPassword) =>
        browser.SubmitAsync("/Account?handler=LinkMicrosoft", new() { ["Password"] = password }, tokenFrom: "/Account");

    /// <summary>A password account made by invite, signed in on /Login in its own browser.</summary>
    protected async Task<(Browser Browser, string Id)> PasswordMemberAsync(string email, string role = "builder")
    {
        var invite = await Owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } });
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accept = await Host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = MemberPassword, useCookie = false });
        Assert.Equal(HttpStatusCode.Created, accept.StatusCode);
        var browser = new Browser(Host);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = MemberPassword })).StatusCode);
        return (browser, (await MeAsync(browser)).Id!);
    }

    protected const string MemberPassword = "a long member passphrase";

    private async Task PostAsync(string path, object body) =>
        Assert.Equal(HttpStatusCode.Created, (await Owner.PostAsJsonAsync(path, body)).StatusCode);

    private sealed class Recorder(ConcurrentQueue<PrivilegesRevoked> received, Func<bool> fail) : INotificationHandler<PrivilegesRevoked>
    {
        public Task Handle(PrivilegesRevoked notification, CancellationToken ct)
        {
            received.Enqueue(notification);
            return fail() ? throw new InvalidOperationException("The memory database is unavailable.") : Task.CompletedTask;
        }
    }

    private sealed class Watching(ConcurrentQueue<Uri> seen) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            seen.Enqueue(request.RequestUri!);
            return base.SendAsync(request, ct);
        }
    }
}
