namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Which teams and departments a person belongs to, read live from the org tree (D055). Implemented by the identity
/// module, which owns the tree; other modules ask through this instead of reading identity's tables. No caching:
/// a membership change is seen by the next request.
/// </summary>
public interface IOrgMembership
{
    /// <summary>Nothing for an unknown user: membership of nothing.</summary>
    Task<OrgMembership> ForUserAsync(string userId, CancellationToken ct);
}
