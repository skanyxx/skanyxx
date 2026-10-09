using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Email;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// D150/D151: with SMTP configured an invite link goes to the invitee and is not returned; without it — or when sending
/// fails — the owner gets the link once, exactly as before. Unusable SMTP settings stop startup, naming the problem.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EmailDeliveryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task WithoutSmtp_TheInviteLinkIsShown_AndNothingIsSent()
    {
        await postgres.ResetAsync();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString);
        await app.BootstrapAsync();
        var owner = (await app.SignInBearerAsync()).AccessToken;

        var invite = await (await app.CreateInviteAsync(owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(invite.GetProperty("emailed").GetBoolean());
        Assert.StartsWith("http://127.0.0.1:", invite.GetProperty("link").GetString());
        Assert.False(app.Services.GetRequiredService<IEmailSender>().IsConfigured);
    }

    [Fact]
    public async Task WithSmtp_TheInviteIsEmailed_AndItsLinkIsNotReturned()
    {
        await postgres.ResetAsync();
        var mail = new FakeEmailSender();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s.AddSingleton<IEmailSender>(mail));
        await app.BootstrapAsync();
        var owner = (await app.SignInBearerAsync()).AccessToken;

        var response = await app.CreateInviteAsync(owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder, SkanyxxRoles.Employee);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var email = await mail.NextAsync(FakeEmailSender.Invite);
        var token = FakeEmailSender.TokenIn(email);
        var accepted = await app.AcceptAsync(token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(invite.GetProperty("emailed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, invite.GetProperty("link").ValueKind);
        Assert.Equal(IdentityApp.MemberEmail, email.To);
        Assert.Contains("builder, employee", email.TextBody);
        Assert.Matches("^skx_inv_[A-Za-z0-9_-]{43}$", token);
        Assert.DoesNotContain(IdentityApp.OwnerPassword, email.TextBody);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task WhenSendingFails_TheOwnerGetsTheLink()
    {
        await postgres.ResetAsync();
        var mail = new FakeEmailSender { Fail = true };
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s.AddSingleton<IEmailSender>(mail));
        await app.BootstrapAsync();
        var owner = (await app.SignInBearerAsync()).AccessToken;

        var invite = await (await app.CreateInviteAsync(owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(invite.GetProperty("emailed").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await app.AcceptAsync(IdentityApp.TokenOf(invite.GetProperty("link").GetString()!))).StatusCode);
        // CR L1: the owner held the invitee's link, and the trail says so.
        var shown = Assert.Single(await postgres.AuditAsync("invite.link_shown"));
        Assert.Equal(invite.GetProperty("inviteId").GetString(), shown.TargetId);
    }

    [Theory]
    [InlineData("smtp.example.com", "None", "skanyxx@example.com", null, "only for a loopback host")]
    [InlineData("smtp.example.com", "StartTls", null, null, "Identity:Smtp:From")]
    [InlineData("smtp.example.com", "StartTls", "not an address", null, "Identity:Smtp:From")]
    [InlineData("smtp.example.com", "StartTls", "skanyxx@example.com", "mailer", "UserName and Password go together")]
    public async Task UnusableSmtpSettings_StopStartup(string host, string security, string? from, string? user, string expected)
    {
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => IdentityApp.StartAsync(postgres.ConnectionString, s =>
        {
            s["Identity:Smtp:Host"] = host;
            s["Identity:Smtp:Security"] = security;
            s["Identity:Smtp:From"] = from;
            s["Identity:Smtp:UserName"] = user;
            s["Identity:Smtp:Password"] = null;
        }));

        Assert.Contains(expected, failure.Message);
    }

    [Fact]
    public async Task ALoopbackRelayWithoutTls_IsAllowed_AndTurnsEmailOn()
    {
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, s =>
        {
            s["Identity:Smtp:Host"] = "localhost";
            s["Identity:Smtp:Port"] = "1025";
            s["Identity:Smtp:Security"] = "None";
            s["Identity:Smtp:From"] = "skanyxx@example.com";
        });

        Assert.True(app.Services.GetRequiredService<IEmailSender>().IsConfigured);
        Assert.True((await app.Client().GetFromJsonAsync<JsonElement>("/api/identity/status", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("passwordResetByEmail").GetBoolean());
    }

    /// <summary>The real MailKit sender against a closed port: a delivery failure, not a crash, and the password never in the message.</summary>
    [Fact]
    public async Task TheSmtpSender_ReportsAnUnreachableServer_WithoutItsSecrets()
    {
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, s =>
        {
            s["Identity:Smtp:Host"] = "127.0.0.1";
            s["Identity:Smtp:Port"] = "1";
            s["Identity:Smtp:Security"] = "None";
            s["Identity:Smtp:From"] = "skanyxx@example.com";
            s["Identity:Smtp:UserName"] = "mailer";
            s["Identity:Smtp:Password"] = "the-smtp-password";
            s["Identity:Smtp:TimeoutSeconds"] = "2";
        });

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() => app.Services.GetRequiredService<IEmailSender>()
            .SendAsync(new EmailMessage(IdentityApp.MemberEmail, "subject", "body with a link"), CancellationToken.None));

        Assert.Contains("127.0.0.1:1", failure.Message);
        Assert.DoesNotContain("the-smtp-password", failure.ToString());
        Assert.DoesNotContain("body with a link", failure.ToString());
    }

    /// <summary>Verifier G3: one failed send is one Warning (LinkMail's), not one from the sender and another from its caller.</summary>
    [Fact]
    public async Task AFailedSend_IsLoggedOnce()
    {
        await postgres.ResetAsync();
        var log = new WarningLog();
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, Smtp("1", "2"), services: s => s.AddSingleton<ILoggerProvider>(log));
        await app.BootstrapAsync();
        var owner = (await app.SignInBearerAsync()).AccessToken;

        var invite = await (await app.CreateInviteAsync(owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(invite.GetProperty("emailed").GetBoolean());
        Assert.Single(log.Warnings, w => w.Contains("127.0.0.1:1"));
    }

    /// <summary>
    /// D168: a server that accepts the connection and never answers holds the caller for about twice the timeout at
    /// most (MailKit's own timeout is per operation), and the caller going away cancels the send at once.
    /// </summary>
    [Fact]
    public async Task ASilentServer_IsBoundedByTheDeadline_AndTheCallerCancels()
    {
        using var silent = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        silent.Start(10);
        var port = ((System.Net.IPEndPoint)silent.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, Smtp(port, "1"));
        var sender = app.Services.GetRequiredService<IEmailSender>();
        var message = new EmailMessage(IdentityApp.MemberEmail, "subject", "body");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(message, CancellationToken.None));
        var bounded = clock.Elapsed;
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        clock.Restart();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(message, caller.Token));
        var cancelled = clock.Elapsed;

        Assert.InRange(bounded, TimeSpan.Zero, TimeSpan.FromSeconds(4));
        Assert.InRange(cancelled, TimeSpan.Zero, TimeSpan.FromMilliseconds(900));
    }

    private static Action<Dictionary<string, string?>> Smtp(string port, string timeoutSeconds) => s =>
    {
        s["Identity:Smtp:Host"] = "127.0.0.1";
        s["Identity:Smtp:Port"] = port;
        s["Identity:Smtp:Security"] = "None";
        s["Identity:Smtp:From"] = "skanyxx@example.com";
        s["Identity:Smtp:TimeoutSeconds"] = timeoutSeconds;
    };
}
