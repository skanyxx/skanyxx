using System.Collections.Concurrent;

namespace Skanyxx.Module.Investigate.Engine;

/// <summary>
/// One executing run: the outputs steps have produced so far, the token every step honours, and the slots that bound how
/// many of its agent steps call kagent at once (<see cref="InvestigateOptions.MaxParallelSteps"/>). The first failure
/// wins and cancels the rest of the run (parallel siblings included); a person's cancel does the same without a reason.
/// </summary>
internal sealed class RunContext(
    Guid runId, string startedBy, string input, IReadOnlyDictionary<string, string> models, int parallelSteps, CancellationToken stopping) : IDisposable
{
    private readonly CancellationTokenSource _cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private string? _failure;
    private int _cancelledByUser;

    public Guid RunId { get; } = runId;

    /// <summary>Whose token quota every step's turn is admitted against and counted to (D385).</summary>
    public string StartedBy { get; } = startedBy;

    public string Input { get; } = input;

    /// <summary>Each agent the run calls (every one passed the check) to its model ref, so its steps' tokens can be priced.</summary>
    public IReadOnlyDictionary<string, string> Models { get; } = models;

    public ConcurrentDictionary<string, string> Outputs { get; } = new();

    /// <summary>Taken around each agent call only, never by a group, so a group waiting on its steps holds none.</summary>
    public SemaphoreSlim Slots { get; } = new(parallelSteps, parallelSteps);

    public CancellationToken Token => _cancel.Token;

    public string? Failure => Volatile.Read(ref _failure);

    public bool CancelledByUser => Volatile.Read(ref _cancelledByUser) == 1;

    public void Fail(string reason)
    {
        Interlocked.CompareExchange(ref _failure, reason, null);
        _cancel.Cancel();
    }

    public void CancelByUser()
    {
        Interlocked.Exchange(ref _cancelledByUser, 1);
        _cancel.Cancel();
    }

    public void Dispose()
    {
        _cancel.Dispose();
        Slots.Dispose();
    }
}
