using System.Globalization;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Email;

namespace Skanyxx.Module.Identity.Email;

/// <summary>
/// The two emails Skanyxx sends (D151, D156). Plain text: what the link is for, the link, when it stops working. Nothing
/// else secret is ever in them. Sent after the change commits, bounded by the sender's own deadline and by
/// <c>ct</c> (the owner's request, or the worker's stop, D168); a failure is one Warning, here, and the caller falls back
/// to showing the link (owner flows) or to nothing (self-service, no oracle).
/// </summary>
internal sealed class LinkMail(IEmailSender sender, ILogger<LinkMail> logger)
{
    public bool Enabled => sender.IsConfigured;

    public Task<bool> InviteAsync(string to, string link, IReadOnlyList<string> roles, DateTimeOffset expires, CancellationToken ct) =>
        SendAsync(new EmailMessage(to, "You are invited to Skanyxx",
            $"""
            You have been invited to Skanyxx with the role(s) {string.Join(", ", roles)}.

            Open this link to choose your password and sign in:
            {link}

            The link works once and expires {Format(expires)}. If you did not expect this invitation, ignore this email.
            """), "invite", ct);

    public Task<bool> PasswordResetAsync(string to, string link, DateTimeOffset expires, CancellationToken ct) =>
        SendAsync(new EmailMessage(to, "Reset your Skanyxx password",
            $"""
            Someone asked to reset the password of your Skanyxx account.

            Open this link to choose a new password:
            {link}

            The link works once and expires {Format(expires)}. Choosing a new password signs you out everywhere.
            If you did not ask for this, ignore this email: your password stays as it is.
            """), "password reset", ct);

    private async Task<bool> SendAsync(EmailMessage message, string kind, CancellationToken ct)
    {
        if (!sender.IsConfigured)
            return false;
        try
        {
            await sender.SendAsync(message, ct);
            return true;
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogWarning("The {Kind} email could not be sent: {Error}", kind, ex.Message);
            return false;
        }
    }

    private static string Format(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
