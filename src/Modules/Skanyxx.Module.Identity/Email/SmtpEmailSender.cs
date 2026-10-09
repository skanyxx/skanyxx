using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Skanyxx.Core.Platform.Email;

namespace Skanyxx.Module.Identity.Email;

/// <summary>
/// SMTP over MailKit (D150): one connection per message (a handful a day; no pool to keep healthy). Plain text only,
/// so nothing a recipient's name or address holds can turn into markup. MailKit's timeout applies per operation, so the
/// whole send is also capped at twice <see cref="SmtpOptions.TimeoutSeconds"/> (D168). A failure surfaces as
/// <see cref="EmailDeliveryException"/> naming the server and the error type — never the body, the recipient or the
/// credentials; the caller logs it, once (G3). The caller's own cancellation goes out as itself.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<IdentityModuleOptions> options) : IEmailSender
{
    public bool IsConfigured => options.Value.Smtp.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var smtp = options.Value.Smtp;
        if (!smtp.IsConfigured)
            throw new InvalidOperationException("SMTP is not configured (Identity:Smtp:Host).");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(smtp.TimeoutSeconds * 2));
        var token = deadline.Token;
        try
        {
            var from = MailboxAddress.Parse(smtp.From!);
            from.Name = smtp.FromName;
            using var mime = new MimeMessage();
            mime.From.Add(from);
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new TextPart(TextFormat.Plain) { Text = message.TextBody };

            using var client = new SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };
            await client.ConnectAsync(smtp.Host!, smtp.Port, smtp.Security switch
            {
                SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
                SmtpSecurity.None => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls
            }, token);
            if (!string.IsNullOrEmpty(smtp.UserName))
                await client.AuthenticateAsync(smtp.UserName, smtp.Password!, token);
            await client.SendAsync(mime, token);
            await client.DisconnectAsync(quit: true, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new EmailDeliveryException($"The SMTP server {smtp.Host}:{smtp.Port} did not accept the message ({ex.GetType().Name}).", ex);
        }
    }
}
