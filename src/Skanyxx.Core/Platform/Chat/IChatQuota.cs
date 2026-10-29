namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// The hook for per-person, per-team and per-department chat limits. Chat asks before every turn and reports what each
/// turn used (also a failed or stopped one); the Chat module registers an unlimited default, the quotas module replaces it.
/// </summary>
public interface IChatQuota
{
    /// <summary>Null when <paramref name="userId"/> may send a turn to <paramref name="agentRef"/>; otherwise why not, in plain words.</summary>
    Task<string?> RefusalAsync(string userId, string agentRef, CancellationToken ct);

    Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct);
}
