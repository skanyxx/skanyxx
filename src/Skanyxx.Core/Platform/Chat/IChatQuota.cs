namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// The hook for per-person and per-team chat limits (a later phase). Chat asks before every turn and reports what each
/// streamed turn used; the Chat module registers an unlimited default, and a quota module replaces it.
/// </summary>
public interface IChatQuota
{
    /// <summary>Null when <paramref name="userId"/> may send a turn to <paramref name="agentRef"/>; otherwise why not, in plain words.</summary>
    Task<string?> RefusalAsync(string userId, string agentRef, CancellationToken ct);

    Task RecordAsync(string userId, string agentRef, string conversationId, ChatUsage usage, CancellationToken ct);
}
