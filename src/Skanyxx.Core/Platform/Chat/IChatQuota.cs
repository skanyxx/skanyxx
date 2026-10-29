namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// The hook for per-person, per-team and per-department token limits. Every model turn run in a person's name asks first
/// (<see cref="AdmitAsync"/>: Chat, Studio previews, the factory and Investigate steps) or is at least counted (ticket
/// stage turns), and reports what it used, also when it failed or was stopped (D383, D385). The
/// default (<see cref="UnlimitedChatQuota"/>) admits everything; the quotas module replaces it.
/// </summary>
public interface IChatQuota
{
    /// <summary>
    /// Admits one turn of <paramref name="userId"/> to <paramref name="agentRef"/>, or says why not (<see cref="ChatQuotaAdmission.Refusal"/>,
    /// in plain words). An admitted turn holds one of the person's in-flight places until the admission is disposed.
    /// </summary>
    Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct);

    /// <summary>
    /// Why <paramref name="userId"/> may not start more work now (over a limit), or null; holds no in-flight place (D387).
    /// For work whose turns come later, a ticket run starting or resuming after its gate: a chat answer on its way must
    /// not refuse it.
    /// </summary>
    Task<string?> RefusalAsync(string userId, CancellationToken ct);

    Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct);
}
