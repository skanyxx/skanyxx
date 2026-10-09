using System.Net;
using MimeKit;

namespace Skanyxx.Module.Identity.Email;

/// <summary>
/// <c>Identity:Smtp</c>. Configured = <see cref="Host"/> set. The password comes from configuration or the environment
/// (<c>Identity__Smtp__Password</c>) only, and is never logged or returned.
/// </summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>The sender address, e.g. <c>skanyxx@example.com</c>.</summary>
    public string? From { get; set; }

    public string FromName { get; set; } = "Skanyxx";

    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrEmpty(Host);

    /// <summary>Null when the settings can be used (or SMTP is off); otherwise what is wrong.</summary>
    internal string? Problem()
    {
        if (!IsConfigured)
            return null;
        if (Host!.Trim() != Host || Host.Any(char.IsWhiteSpace))
            return "Identity:Smtp:Host must be a host name without spaces.";
        if (Port is < 1 or > 65535)
            return "Identity:Smtp:Port must be 1–65535.";
        if (TimeoutSeconds is < 1 or > 120)
            return "Identity:Smtp:TimeoutSeconds must be 1–120.";
        if (string.IsNullOrEmpty(From) || !MailboxAddress.TryParse(From, out var from) || from.Address.IndexOf('@') <= 0)
            return "Identity:Smtp:From must be the sender's email address.";
        if (string.IsNullOrEmpty(UserName) != string.IsNullOrEmpty(Password))
            return "Identity:Smtp:UserName and Password go together: set both, or neither for a relay that needs no sign-in.";
        if (Security == SmtpSecurity.None && !IsLoopback(Host))
            return "Identity:Smtp:Security None sends links (and any password) in clear text: only for a loopback host. Use StartTls or SslOnConnect.";
        return null;
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
}
