namespace Skanyxx.Core.Platform.Email;

/// <summary>
/// Outgoing email (invites, password-reset links). Implemented by the identity module over SMTP; when SMTP is not
/// configured, <see cref="IsConfigured"/> is false and callers keep their no-email behaviour (the owner copies links).
/// </summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    /// <summary>Throws <see cref="EmailDeliveryException"/> when the message could not be handed to the SMTP server.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
