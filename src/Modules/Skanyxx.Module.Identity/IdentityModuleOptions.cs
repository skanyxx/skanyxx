using System.ComponentModel.DataAnnotations;
using Npgsql;
using Skanyxx.Module.Identity.Email;

namespace Skanyxx.Module.Identity;

public sealed class IdentityModuleOptions
{
    public const string Section = "Identity";
    public const int MinBootstrapTokenLength = 32;

    /// <summary>Bound from <c>ConnectionStrings:Identity</c>.</summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// When set (at least <see cref="MinBootstrapTokenLength"/> characters), <c>POST api/identity/bootstrap</c>, the
    /// setup page and <c>POST api/identity/unlock</c> require it in <c>X-Bootstrap-Token</c>. When empty, the owner can
    /// be created only in Development and only from a direct loopback connection; elsewhere setup is refused.
    /// </summary>
    public string? BootstrapToken { get; set; }

    /// <summary>Length is the policy (NIST 800-63B); no composition rules.</summary>
    [Range(12, 128)]
    public int PasswordMinLength { get; set; } = 12;

    [Range(1, 100)]
    public int LockoutMaxFailedAttempts { get; set; } = 5;

    [Range(1, 24 * 60)]
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Absolute session length from the password sign-in, for the cookie and for a refresh-token chain alike.</summary>
    [Range(1, 90)]
    public int SessionDays { get; set; } = 7;

    /// <summary>
    /// Where people reach Skanyxx (e.g. <c>https://skanyxx.example.com</c>), for the invite links. https, except for a
    /// loopback host (the desktop installs) or in Development: behind a TLS-terminating proxy the request itself says
    /// <c>http</c> and an internal host, which would put the token in cleartext or on a host nobody can reach. Unset: the
    /// app starts, but outside Development invites are refused until it is set (Development uses the request's).
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>How long an invite link can be accepted.</summary>
    [Range(1, 30)]
    public int InviteDays { get; set; } = 7;

    /// <summary>
    /// PFX (PKCS#12), with its private key, that encrypts the Data Protection key ring at rest; any other format (PEM,
    /// DER) stops startup, as does a PFX beyond <c>Pkcs12LoaderLimits.Defaults</c>. Unset: keys are stored in plain text (warned outside Development).
    /// </summary>
    public string? DataProtectionCertificatePath { get; set; }

    public string? DataProtectionCertificatePassword { get; set; }

    [Range(1, 1_000)]
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>Outgoing email (D150). Unset (<see cref="SmtpOptions.Host"/> empty): invite links are copied by the owner, as before, and nobody can reset a password by email.</summary>
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>How long a password-reset link works (D156); one use, and a newer link for the same account revokes it.</summary>
    [Range(5, 24 * 60)]
    public int PasswordResetMinutes { get; set; } = 60;

    /// <summary>
    /// Audit rows, and invites that are no longer pending, older than this are deleted by the retention job (D155).
    /// </summary>
    [Range(1, 3650)]
    public int AuditRetentionDays { get; set; } = 365;

    /// <summary>How often the groups of Entra-managed accounts are re-read from Microsoft Graph while Microsoft sign-in is on (D158).</summary>
    [Range(5, 24 * 60)]
    public int EntraRecheckMinutes { get; set; } = 60;

    /// <summary>The parsed <see cref="PublicBaseUrl"/>; null when unset. Links are built from this, not the raw string.</summary>
    internal Uri? PublicBaseUri => string.IsNullOrEmpty(PublicBaseUrl) ? null : new Uri(PublicBaseUrl, UriKind.Absolute);

    /// <summary>
    /// Unset, or taken exactly as written: no whitespace, control characters or backslashes (<see cref="Uri"/> would
    /// quietly trim or rewrite them); absolute http(s) with no query, fragment or user info; https unless
    /// <paramref name="development"/> or a loopback host, whose links never cross a network.
    /// </summary>
    internal bool HasValidPublicBaseUrl(bool development) =>
        string.IsNullOrEmpty(PublicBaseUrl)
        || (!PublicBaseUrl.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')
            && Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && (development || url.IsLoopback)))
            && url.Query.Length == 0 && url.Fragment.Length == 0 && url.UserInfo.Length == 0);

    /// <summary>The configured string with this module's own pool (see MemoryOptions for why the Application Name differs).</summary>
    internal NpgsqlConnectionStringBuilder ConnectionSettings()
    {
        var settings = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = MaxPoolSize };
        settings.ApplicationName = $"{(string.IsNullOrEmpty(settings.ApplicationName) ? "skanyxx" : settings.ApplicationName)}-identity";
        return settings;
    }
}
