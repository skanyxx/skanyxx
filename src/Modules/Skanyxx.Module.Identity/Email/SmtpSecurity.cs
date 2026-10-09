namespace Skanyxx.Module.Identity.Email;

/// <summary>How the SMTP connection is protected. <see cref="None"/> only to a loopback host (a local relay or catcher).</summary>
public enum SmtpSecurity
{
    /// <summary>Plain connect, then STARTTLS, which must succeed (port 587).</summary>
    StartTls,

    /// <summary>TLS from the first byte (port 465).</summary>
    SslOnConnect,

    None
}
