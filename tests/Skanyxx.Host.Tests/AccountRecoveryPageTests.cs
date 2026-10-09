using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Email;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The identity leftovers (section 6) on the real host: "Forgot your password?" and the reset page (D156), the owner's
/// reset link on People (D157), the Audit page (D152), and a disable that cannot stop the person's sandbox tasks (D154).
/// </summary>
[Collection(HostCollection.Name)]
public sealed partial class AccountRecoveryPageTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string MemberEmail = "bea@skanyxx.example";
    private const string MemberPassword = "a long member passphrase";
    private const string NewPassword = "a brand new passphrase";

    private readonly HostEmail _mail = new();
    private HostApp _host = null!;
    private Browser _owner = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), HostApp.WithoutSandboxes, services: s => s.AddSingleton<IEmailSender>(_mail));
        _owner = await OwnerBrowserAsync(_host);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task ForgotAndReset_InTheBrowser()
    {
        await MemberAsync(_host, "employee");
        var anonymous = new Browser(_host);

        var login = await (await anonymous.GetAsync("/Login")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var forgot = await anonymous.SubmitAsync("/ForgotPassword", new() { ["Email"] = MemberEmail });
        var forgotUnknown = await anonymous.SubmitAsync("/ForgotPassword", new() { ["Email"] = "nobody@skanyxx.example" });
        var token = await _mail.TokenAsync("password");
        var page = await anonymous.GetAsync("/ResetPassword?token=" + token);
        var reset = await anonymous.SubmitAsync("/ResetPassword", new() { ["Token"] = token, ["Password"] = NewPassword },
            tokenFrom: "/ResetPassword?token=" + token);
        var after = await (await anonymous.GetAsync("/Login?reset=true")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var signIn = await new Browser(_host).SubmitAsync("/Login", new() { ["Email"] = MemberEmail, ["Password"] = NewPassword });
        var reused = await new Browser(_host).GetAsync("/ResetPassword?token=" + token);

        Assert.Contains("href=\"/ForgotPassword\"", login);
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var answer = Regex.Match(await forgot.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "role=\"status\">([^<]+)<").Groups[1].Value;
        Assert.Contains("a link is on its way", answer);
        Assert.Contains(answer, await forgotUnknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)); // the same answer for an unknown email
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(MemberEmail, await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
        Assert.Matches($"^{Regex.Escape(HostApp.AllowedOrigin)}/ResetPassword\\?token=skx_rst_", _mail.Sent.Single(m => m.Subject.Contains("password")).TextBody.Split('\n').Single(l => l.Contains("token=")).Trim());
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/Login?reset=true", reset.Headers.Location?.OriginalString);
        Assert.Contains("Your password was changed", after);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);
    }

    [Fact]
    public async Task WithoutSmtp_ThereIsNoForgotLink_AndPeopleShowsTheResetLinkOnce()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), HostApp.WithoutSandboxes);
        var owner = await OwnerBrowserAsync(host);
        await MemberAsync(host, "builder");
        var memberId = await PersonIdAsync(host, MemberEmail);

        var login = await (await new Browser(host).GetAsync("/Login")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var forgotPage = await new Browser(host).GetAsync("/ForgotPassword");
        var people = await (await owner.GetAsync("/People")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var issued = await owner.SubmitAsync($"/People?handler=Reset&id={memberId}", [], tokenFrom: "/People");
        var html = await issued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var link = ResetLink().Match(html).Groups[1].Value;
        var later = await (await owner.GetAsync("/People")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var reset = await new Browser(host).SubmitAsync("/ResetPassword", new() { ["Token"] = Uri.UnescapeDataString(link.Split("token=")[1]), ["Password"] = NewPassword },
            tokenFrom: "/ResetPassword?token=" + link.Split("token=")[1]);

        Assert.DoesNotContain("/ForgotPassword", login);
        Assert.Equal(HttpStatusCode.NotFound, forgotPage.StatusCode);
        Assert.Contains($"id={memberId}&amp;handler=Reset", people);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.True(issued.Headers.CacheControl?.NoStore);
        Assert.StartsWith($"{HostApp.AllowedOrigin}/ResetPassword?token=skx_rst_", link);
        Assert.DoesNotContain("skx_rst_", later);
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
    }

    [Fact]
    public async Task TheAuditPage_IsTheOwnersOnly_AndNamesPeople()
    {
        var member = await MemberAsync(_host, "supervisor");

        var ownerNav = await (await _owner.GetAsync("/Hooks")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var page = await _owner.GetAsync("/Audit");
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var filtered = await (await _owner.GetAsync("/Audit?action=invite.accepted")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var memberNav = await (await member.GetAsync("/Hooks")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var memberPage = await member.GetAsync("/Audit");

        Assert.Contains("href=\"/Audit\"", ownerNav);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("invite.created", html);
        Assert.Contains("invite.accepted", html);
        Assert.Contains(HostApp.OwnerEmail, html); // the actor's id shown as their email
        Assert.DoesNotContain("invite.created", filtered);
        Assert.DoesNotContain("href=\"/Audit\"", memberNav);
        Assert.Equal(HttpStatusCode.Redirect, memberPage.StatusCode);
    }

    /// <summary>
    /// D154 on the real host: sandboxes on and AX unreachable (the test default), a disable cannot stop the person's
    /// tasks, so it answers 500 after saving — the person is disabled — and its body says what did not happen and how to retry; a role change without
    /// supervisor asks AX nothing and stays 200.
    /// </summary>
    [Fact]
    public async Task ADisable_ThatCannotStopTheSandboxTasks_Is500_ButTheAccountIsDisabled()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        var owner = await host.OwnerAsync();
        var invite = await (await owner.PostAsJsonAsync("/api/identity/invites", new { email = MemberEmail, roles = new[] { "supervisor" } }, cancellationToken: TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var accepted = await (await host.Client().PostAsJsonAsync("/api/identity/invites/accept", new
        {
            token = Uri.UnescapeDataString(invite.GetProperty("link").GetString()!.Split("token=")[1]), password = MemberPassword, useCookie = false
        }, cancellationToken: TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var memberId = accepted.GetProperty("user").GetProperty("id").GetString()!;

        var roles = await owner.PutAsJsonAsync($"/api/identity/people/{memberId}/roles", new { roles = new[] { "employee" } }, cancellationToken: TestContext.Current.CancellationToken);
        var disable = await owner.PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        var signIn = await host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = MemberEmail, password = MemberPassword }, cancellationToken: TestContext.Current.CancellationToken);
        var people = await owner.GetFromJsonAsync<JsonElement>("/api/identity/people", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, disable.StatusCode);
        Assert.Equal(DisableRetry, (await disable.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        Assert.True(people.EnumerateArray().Single(p => p.GetProperty("id").GetString() == memberId).GetProperty("disabled").GetBoolean());
    }

    /// <summary>
    /// Verifier G2 / D162: the same failure from the People page is the page with the change's result and the retry
    /// hint, not the generic error page; the account is disabled.
    /// </summary>
    [Fact]
    public async Task ADisableFromThePage_ThatCannotStopTheSandboxTasks_SaysSoAndHowToRetry()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        var owner = await OwnerBrowserAsync(host);
        await MemberAsync(host, "builder");
        var memberId = await PersonIdAsync(host, MemberEmail);

        var disable = await owner.SubmitAsync($"/People?handler=Disable&id={memberId}", [], tokenFrom: "/People");
        var html = System.Net.WebUtility.HtmlDecode(await disable.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var signIn = await host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = MemberEmail, password = MemberPassword }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, disable.StatusCode);
        Assert.Contains(DisableRetry, html);
        Assert.Contains("People", html); // the People page itself, with its list
        Assert.Contains(MemberEmail, html);
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }

    /// <summary>
    /// QA-2 L3 / D162: a roles save on a disabled account whose sandbox tasks cannot be stopped says the roles are saved,
    /// what did not happen and how to retry — through the API and on the People page.
    /// </summary>
    [Fact]
    public async Task ARolesSave_OnADisabledAccount_ThatCannotStopTheSandboxTasks_SaysSoAndHowToRetry()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        var browser = await OwnerBrowserAsync(host);
        await MemberAsync(host, "builder");
        var memberId = await PersonIdAsync(host, MemberEmail);
        var owner = await host.OwnerAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, (await owner.PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken)).StatusCode);

        var api = await owner.PutAsJsonAsync($"/api/identity/people/{memberId}/roles", new { roles = new[] { "employee" } }, cancellationToken: TestContext.Current.CancellationToken);
        var page = await browser.SubmitAsync($"/People?handler=Roles&id={memberId}", new() { ["roles"] = "builder" }, tokenFrom: "/People");
        var html = System.Net.WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var people = await owner.GetFromJsonAsync<JsonElement>("/api/identity/people", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, api.StatusCode);
        Assert.Equal(RolesRetry, (await api.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.InternalServerError, page.StatusCode);
        Assert.Contains(RolesRetry, html);
        Assert.Equal(["builder"], people.EnumerateArray().Single(p => p.GetProperty("id").GetString() == memberId)
            .GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    private const string RolesRetry = "Roles saved. Its sandbox tasks could not be stopped (AX unavailable) — save again to retry.";

    private const string DisableRetry = "Account disabled. Its sandbox tasks could not be stopped (AX unavailable) — disable again to retry.";

    private static async Task<Browser> OwnerBrowserAsync(HostApp host)
    {
        var owner = new Browser(host);
        var setup = await owner.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
        return owner;
    }

    /// <summary>Invited by the owner (through email when the host has SMTP, else the shown link), accepted, signed in.</summary>
    private async Task<Browser> MemberAsync(HostApp host, string role)
    {
        var owner = await host.OwnerAsync();
        var invite = await (await owner.PostAsJsonAsync("/api/identity/invites", new { email = MemberEmail, roles = new[] { role } }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var token = invite.GetProperty("link").GetString() is { } link
            ? Uri.UnescapeDataString(link.Split("token=")[1])
            : await _mail.TokenAsync("invited");
        Assert.Equal(HttpStatusCode.Created, (await host.Client().PostAsJsonAsync("/api/identity/invites/accept",
            new { token, password = MemberPassword, useCookie = false })).StatusCode);
        var member = new Browser(host);
        Assert.Equal(HttpStatusCode.Redirect, (await member.SubmitAsync("/Login", new() { ["Email"] = MemberEmail, ["Password"] = MemberPassword })).StatusCode);
        return member;
    }

    private static async Task<string> PersonIdAsync(HostApp host, string email) =>
        (await (await host.OwnerAsync()).GetFromJsonAsync<JsonElement>("/api/identity/people")).EnumerateArray()
            .Single(p => p.GetProperty("email").GetString() == email).GetProperty("id").GetString()!;

    [GeneratedRegex("id=\"resetLink\" readonly value=\"([^\"]+)\"")]
    private static partial Regex ResetLink();

    /// <summary>SMTP as the identity module sees it: every message kept.</summary>
    private sealed class HostEmail : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public bool IsConfigured => true;

        public IReadOnlyList<EmailMessage> Sent => [.. _sent];

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            _sent.Enqueue(message);
            return Task.CompletedTask;
        }

        /// <summary>The token of the newest message whose subject has <paramref name="kind"/>, waiting for it (resets are sent in the background).</summary>
        public async Task<string> TokenAsync(string kind)
        {
            for (var i = 0; i < 500; i++)
            {
                if (_sent.LastOrDefault(m => m.Subject.Contains(kind)) is { } message)
                    return Uri.UnescapeDataString(Regex.Match(message.TextBody, "token=([^\\s]+)").Groups[1].Value);
                await Task.Delay(20);
            }
            throw new Xunit.Sdk.XunitException($"No '{kind}' email was sent.");
        }
    }
}
