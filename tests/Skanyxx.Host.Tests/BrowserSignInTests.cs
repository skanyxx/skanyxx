using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Skanyxx.Host.Tests;

/// <summary>The Razor flow on a fresh database: setup creates the owner and signs in, login and logout, antiforgery.</summary>
[Collection(HostCollection.Name)]
public sealed class BrowserSignInTests(PostgresFixture fixture) : IAsyncLifetime
{
    private HostApp _host = null!;
    private string _database = null!;

    private static readonly Dictionary<string, string> Owner = new()
    {
        ["Email"] = HostApp.OwnerEmail,
        ["Password"] = HostApp.OwnerPassword,
        ["DisplayName"] = "Ana Owner",
        ["BootstrapToken"] = HostApp.BootstrapToken
    };

    public async ValueTask InitializeAsync()
    {
        _database = await fixture.NewDatabaseAsync();
        _host = await HostApp.StartAsync(_database);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task BeforeSetup_LoginRedirectsToSetup()
    {
        var response = await _host.Client().GetAsync("/Login", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Setup", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Setup_CreatesTheOwner_SignsIn_AndShowsTheUserInTheNav()
    {
        var browser = new Browser(_host);

        var setup = await browser.SubmitAsync("/Setup", Owner);
        var page = await browser.GetAsync("/Hooks");
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        // The first hour goes on with the owner's model step (D5).
        Assert.Equal("/Model", setup.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Ana Owner", html);
        Assert.Contains("Sign out", html);
    }

    [Fact]
    public async Task AfterSetup_SetupRedirectsToLogin_AndBootstrapIs409()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);

        var page = await _host.Client().GetAsync("/Setup", TestContext.Current.CancellationToken);
        var api = await _host.BootstrapAsync(); // with the token: the guard passes, the owner already exists

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/Login", page.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Conflict, api.StatusCode);
    }

    [Fact]
    public async Task Setup_ThroughAProxy_WithoutToken_IsRefused()
    {
        var browser = new Browser(_host);
        var html = await (await browser.GetAsync("/Setup")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        var response = await browser.PostFormAsync("/Setup",
            new Dictionary<string, string>(Owner) { ["BootstrapToken"] = "", ["__RequestVerificationToken"] = token },
            r => r.Headers.Add("X-Forwarded-For", "203.0.113.9"));
        var status = await _host.Client().GetFromJsonAsync<JsonElement>("/api/identity/status", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the form again, with the error
        Assert.Contains("bootstrap token", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(status.GetProperty("bootstrapped").GetBoolean());
    }

    /// <summary>SEC S2: outside Development a plain loopback connection is not enough; only the token creates the owner.</summary>
    [Fact]
    public async Task Setup_FromLoopback_WithoutToken_IsRefusedOutsideDevelopment()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s => s["Identity:BootstrapToken"] = "");

        var response = await new Browser(host).SubmitAsync("/Setup", new Dictionary<string, string>(Owner) { ["BootstrapToken"] = "" });
        var api = await host.BootstrapAsync(token: null);
        var status = await host.Client().GetFromJsonAsync<JsonElement>("/api/identity/status", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Identity:BootstrapToken", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
        Assert.False(status.GetProperty("bootstrapped").GetBoolean());
    }

    /// <summary>CR m1 / SEC S9: empty fields bind as null, and a NUL must not reach Postgres; the form comes back as a 400.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("owner\0@skanyxx.example", "correct horse battery")]
    public async Task Forms_WithMissingOrControlCharacterInput_Are400(string email, string password)
    {
        var setup = await new Browser(_host).SubmitAsync("/Setup",
            new() { ["Email"] = email, ["Password"] = password, ["BootstrapToken"] = HostApp.BootstrapToken });
        await new Browser(_host).SubmitAsync("/Setup", Owner);
        var login = await new Browser(_host).SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password });

        Assert.Equal(HttpStatusCode.BadRequest, setup.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsTheGenericError_RightPassword_ReturnsToThePage()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);
        var browser = new Browser(_host);

        var wrong = await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = "not the password" });
        var right = await browser.SubmitAsync("/Login?ReturnUrl=%2FHooks",
            new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["ReturnUrl"] = "/Hooks" });

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Invalid email or password.", await wrong.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.Equal("/Hooks", right.Headers.Location?.OriginalString);
        Assert.True(browser.HasCookie("skanyxx.auth"));
    }

    /// <summary>SEC2-N2: the form's optional bootstrap token field is the owner's way past a lockout someone else keeps up.</summary>
    [Fact]
    public async Task Login_OwnerWithBootstrapToken_SignsInPastTheLockout()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);
        for (var i = 0; i < 5; i++)
            await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = HostApp.OwnerEmail, password = "wrong password " + i }, cancellationToken: TestContext.Current.CancellationToken);
        var fields = new Dictionary<string, string> { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword };

        var locked = await new Browser(_host).SubmitAsync("/Login", fields);
        var breakGlass = await new Browser(_host).SubmitAsync("/Login", new(fields) { ["BootstrapToken"] = HostApp.BootstrapToken });

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("Invalid email or password.", await locked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Redirect, breakGlass.StatusCode);
    }

    /// <summary>CR3 m1: a second sign-in in flight for the account (a double-click) is told to retry, not "invalid".</summary>
    [Fact]
    public async Task Login_WhileAnotherSignInHoldsTheAccount_SaysTryAgain()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);
        var browser = new Browser(_host);
        await using var connection = new NpgsqlConnection(_database);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        // The key an in-flight sign-in holds: AccountLock, seed 0x49444E03.
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('OWNER@SKANYXX.EXAMPLE', 1229213187))", connection, transaction))
            await hold.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        var response = await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign-in is in progress for this account; try again.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>CR3 m1 / SEC3 R3-5: the button disables itself after the first click, and the token field is bounded.</summary>
    [Fact]
    public async Task LoginForm_DisablesSubmitOnce_AndBoundsTheToken()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);

        var html = await (await _host.Client().GetAsync("/Login", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("onsubmit=\"this.querySelector('button[type=submit]').disabled = true\"", html);
        Assert.Matches("<input(?=[^>]*name=\"BootstrapToken\")(?=[^>]*maxlength=\"512\")", html);
    }

    [Fact]
    public async Task Login_WithABootstrapTokenOver512Characters_Is400()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);

        var response = await new Browser(_host).SubmitAsync("/Login", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = new string('x', 513)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_AbsoluteReturnUrl_IsNotFollowed()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);

        var response = await new Browser(_host).SubmitAsync("/Login",
            new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["ReturnUrl"] = "https://evil.example/" });

        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_Is400()
    {
        await new Browser(_host).SubmitAsync("/Setup", Owner);

        var response = await new Browser(_host).PostFormAsync("/Login",
            new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsTheSession()
    {
        var browser = new Browser(_host);
        await browser.SubmitAsync("/Setup", Owner);

        var logout = await browser.SubmitAsync("/Logout", [], tokenFrom: "/Hooks");
        var after = await browser.GetAsync("/Hooks");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/Login", logout.Headers.Location?.OriginalString);
        Assert.False(browser.HasCookie("skanyxx.auth"));
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    /// <summary>SEC S3: the logout page is a real sign-out: it rotates the stamp, so the user's refresh tokens die too.</summary>
    [Fact]
    public async Task Logout_EndsTheUsersOtherSessions()
    {
        var browser = new Browser(_host);
        await browser.SubmitAsync("/Setup", Owner);
        var signIn = await _host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = HostApp.OwnerEmail, password = HostApp.OwnerPassword }, cancellationToken: TestContext.Current.CancellationToken);
        var refreshToken = (await signIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("tokens").GetProperty("refreshToken").GetString();

        await browser.SubmitAsync("/Logout", [], tokenFrom: "/Hooks");
        var refresh = await _host.Client().PostAsJsonAsync("/api/identity/refresh", new { refreshToken }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task SessionCookie_IsHttpOnly_Lax_AndSecureOutsideDevelopment()
    {
        var html = await (await _host.Client().GetAsync("/Setup", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var browser = new Browser(_host);

        var setup = await browser.SubmitAsync("/Setup", Owner);
        var cookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.ParseList(setup.Headers.GetValues("Set-Cookie").ToList())
            .Single(c => c.Name == "skanyxx.auth");

        Assert.Contains("__RequestVerificationToken", html);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure); // HostApp runs as Production
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
    }
}
