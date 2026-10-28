namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>How the last reconcile pass ended: when, how many agents it could not apply, and whether it failed outright.</summary>
internal sealed record ReconcilePass(DateTimeOffset At, int Failures, bool Failed);
