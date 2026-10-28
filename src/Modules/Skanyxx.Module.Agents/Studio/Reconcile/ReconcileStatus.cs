namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>The last pass of this replica's reconcile loop, for the Dashboard's background-jobs tile. Null until a pass ran.</summary>
internal sealed class ReconcileStatus
{
    private volatile ReconcilePass? _last;

    public ReconcilePass? Last => _last;

    public void Record(ReconcilePass pass) => _last = pass;
}
