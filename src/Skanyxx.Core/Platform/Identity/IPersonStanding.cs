namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// What a person may do right now, read live from identity. Work that runs on a person's authority after their request
/// has ended (ticket stages, investigation steps, sandboxes) asks this when it picks the work up, since a role taken
/// away or an account disabled in between must stop it (D395). No caching.
/// </summary>
public interface IPersonStanding
{
    /// <summary>The person's roles while the account is enabled; none for a disabled or unknown account.</summary>
    Task<IReadOnlySet<string>> ActiveRolesAsync(string userId, CancellationToken ct);
}
