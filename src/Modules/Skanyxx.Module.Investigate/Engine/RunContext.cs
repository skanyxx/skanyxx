using System.Collections.Concurrent;

namespace Skanyxx.Module.Investigate.Engine;

/// <summary>
/// One executing run: the outputs steps have produced so far, and the token every step honours. The first failure
/// wins and cancels the rest of the run (parallel siblings included); a person's cancel does the same without a reason.
/// </summary>
internal sealed class RunContext(Guid runId, string input, CancellationToken stopping) : IDisposable
{
    private readonly CancellationTokenSource _cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private string? _failure;
    private int _cancelledByUser;

    public Guid RunId { get; } = runId;

    public string Input { get; } = input;

    public ConcurrentDictionary<string, string> Outputs { get; } = new();

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

    public void Dispose() => _cancel.Dispose();
}
