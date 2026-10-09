using Skanyxx.Core.Platform.Email;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>SMTP as the module sees it: configured or not, failing on demand, and every message kept for the test to read.</summary>
public sealed class FakeEmailSender(bool configured = true) : IEmailSender
{
    public const string Reset = "password";
    public const string Invite = "invited";

    private readonly List<EmailMessage> _sent = [];
    private readonly HashSet<int> _taken = [];

    public bool IsConfigured => configured;

    public volatile bool Fail;

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
                return [.. _sent];
        }
    }

    /// <summary>The password-reset emails sent so far.</summary>
    public IReadOnlyList<EmailMessage> Resets => [.. Sent.Where(m => m.Subject.Contains(Reset))];

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Fail)
            throw new EmailDeliveryException("The SMTP server smtp.example:587 did not accept the message (SocketException).");
        lock (_sent)
            _sent.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The next message of a kind (<see cref="Reset"/>, <see cref="Invite"/>) not taken yet, waiting for it: the reset
    /// worker sends after the request has answered.
    /// </summary>
    public async Task<EmailMessage> NextAsync(string kind = Reset, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            lock (_sent)
                for (var i = 0; i < _sent.Count; i++)
                    if (!_taken.Contains(i) && _sent[i].Subject.Contains(kind))
                    {
                        _taken.Add(i);
                        return _sent[i];
                    }
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException($"No '{kind}' email was sent.");
    }

    /// <summary>The token in a message's one-time link.</summary>
    public static string TokenIn(EmailMessage message) =>
        Uri.UnescapeDataString(System.Text.RegularExpressions.Regex.Match(message.TextBody, "token=([^\\s]+)").Groups[1].Value);
}
