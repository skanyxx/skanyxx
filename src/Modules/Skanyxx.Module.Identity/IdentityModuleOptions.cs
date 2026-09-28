using System.ComponentModel.DataAnnotations;
using Npgsql;

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

    /// <summary>How often a cookie is re-checked against the user's security stamp; 0 checks on every request.</summary>
    [Range(0, 3600)]
    public int SecurityStampValidationSeconds { get; set; } = 60;

    /// <summary>PFX that encrypts the Data Protection key ring at rest. Unset: keys are stored in plain text (warned outside Development).</summary>
    public string? DataProtectionCertificatePath { get; set; }

    public string? DataProtectionCertificatePassword { get; set; }

    [Range(1, 1_000)]
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>The configured string with this module's own pool (see MemoryOptions for why the Application Name differs).</summary>
    internal NpgsqlConnectionStringBuilder ConnectionSettings()
    {
        var settings = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = MaxPoolSize };
        settings.ApplicationName = $"{(string.IsNullOrEmpty(settings.ApplicationName) ? "skanyxx" : settings.ApplicationName)}-identity";
        return settings;
    }
}
