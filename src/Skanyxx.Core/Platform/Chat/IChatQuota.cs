namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// The hook for per-person, per-team and per-department token limits. Every model turn run in a person's name asks first
/// (<see cref="AdmitAsync"/>: Chat, Studio previews and the factory) or is at least counted (ticket stage turns), and
/// reports what it used, also when it failed or was stopped. Investigate must do the same once it is merged (D383). The
/// default (<see cref="UnlimitedChatQuota"/>) admits everything; the quotas module replaces it.
/// </summary>
public interface IChatQuota
{
    /// <summary>
    /// Admits one turn of <paramref name="userId"/> to <paramref name="agentRef"/>, or says why not (<see cref="ChatQuotaAdmission.Refusal"/>,
    /// in plain words). An admitted turn holds one of the person's in-flight places until the admission is disposed.
    /// </summary>
    Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct);

    Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct);
}
