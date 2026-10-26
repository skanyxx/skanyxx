namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <param name="Failures">Agents and plugin servers that failed to apply or be removed; the next pass retries them.</param>
/// <param name="Braked">Removals the removal brake held (D119): they wait for the owner's confirmed pass, not for a retry.</param>
/// <param name="KeptInUse">
/// Plugin servers that left main but stay, because agents in main still use them (D219): they wait for a person to change
/// those agents, not for a retry.
/// </param>
internal sealed record ReconcileResult(int Failures, int Braked, IReadOnlyList<string> KeptInUse);
