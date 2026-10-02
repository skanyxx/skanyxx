namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>AX v0.3.1 phases: Pending, Running, Suspended, Failed, Terminating (Completed is defined but never set).</summary>
public static class TaskPhases
{
    public const string Suspended = "Suspended";

    /// <summary>Suspend applies to a Pending/Running task, resume to a Suspended one: neither may reactivate a task.</summary>
    public static bool CanSetSuspended(string phase, bool suspend) =>
        suspend ? phase is "Pending" or "Running" : phase == Suspended;

    /// <summary>Counts toward the per-user cap: anything not failed, completed or already being deleted.</summary>
    public static bool IsActive(string phase) => phase is not ("Failed" or "Completed" or "Terminating");
}
