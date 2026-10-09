namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>The reconciler cannot apply an agent for a reason a person can act on; the message says what.</summary>
internal class StudioApplyException(string message) : Exception(message);
