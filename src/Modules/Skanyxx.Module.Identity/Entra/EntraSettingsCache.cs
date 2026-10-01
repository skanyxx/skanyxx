using System.Data;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// The saved settings as this instance uses them (<see cref="Current"/>). Loaded before Kestrel listens, reloaded right
/// after a save here and, for saves on other replicas, by <see cref="EntraSettingsRefresher"/>. A reload that finds a
/// newer version publishes it first and then drops the cached <see cref="OpenIdConnectOptions"/>, so the next request
/// builds them — authority, client, secret and a new metadata ConfigurationManager — from the new values, without a
/// restart (A3). The cache stores a lazy entry before building it, so removing it after publishing leaves no stale entry.
/// </summary>
internal sealed class EntraSettingsCache(
    IDataProtectionProvider protection, IOptionsMonitorCache<OpenIdConnectOptions> options, ILogger<EntraSettingsCache> logger)
{
    private const string SecretPurpose = "Skanyxx.Identity.Entra.ClientSecret";

    private readonly object _gate = new();
    private EntraConfig _current = EntraConfig.Disabled;

    public EntraConfig Current => Volatile.Read(ref _current);

    public string Protect(string secret) => Protector.Protect(secret);

    /// <summary>One primary-key read when nothing changed; the full settings only when the stored version is newer.</summary>
    public async Task RefreshAsync(AccountsDbContext db, CancellationToken ct)
    {
        // One snapshot for the three reads: a save landing between them cannot mix one version's row with another's groups.
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var version = await db.EntraSettings.Select(s => (int?)s.Version).SingleOrDefaultAsync(ct);
        if (version is null || version <= Current.Version)
            return;

        var row = await db.EntraSettings.AsNoTracking().SingleAsync(ct);
        var groups = await db.EntraGroups.AsNoTracking().ToListAsync(ct);
        await snapshot.CommitAsync(ct);
        var loaded = new EntraConfig(row.Enabled, row.TenantId, row.ClientId, Unprotect(row.ProtectedClientSecret),
            groups.ToDictionary(g => g.GroupId, g => new EntraGroupMapDto(g.GroupId, g.Label, g.Roles, g.Teams)), row.Version);

        lock (_gate)
        {
            // Two reloads can race (a save here and the periodic one); the newer version wins.
            if (loaded.Version <= _current.Version)
                return;
            Volatile.Write(ref _current, loaded);
        }
        options.TryRemove(EntraScheme.Name);
        logger.LogInformation("Microsoft sign-in settings version {Version} loaded: {State}", loaded.Version, loaded.CanSignIn ? "on" : "off");
    }

    private IDataProtector Protector => protection.CreateProtector(SecretPurpose);

    private string? Unprotect(string? protectedSecret)
    {
        if (protectedSecret is null)
            return null;
        try
        {
            return Protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            logger.LogError("The stored Entra client secret cannot be decrypted (its Data Protection key is gone); Microsoft sign-in is off " +
                "until the owner enters the secret again on the Microsoft sign-in page.");
            return null;
        }
    }
}
