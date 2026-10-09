namespace Skanyxx.Module.Identity.Audit;

/// <summary>
/// D167: refusals any caller can cause without an account — unknown "forgot" emails, invalid link lookups and accepts,
/// failed Microsoft callbacks, owner-route refusals — are recorded at most once per <see cref="Window"/> per kind on
/// each replica. The recorded row carries how many of that kind were left out since the previous one, so the trail
/// still shows the volume; the Warning line is still written for every one of them. Per replica, in memory: a restart
/// loses an open count, which is at most one window's worth.
/// </summary>
internal sealed class AuditSampler(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, (DateTimeOffset Last, int Skipped)> _kinds = [];

    /// <summary>Null when this one is left out; otherwise how many of its kind were left out before it.</summary>
    public int? Admit(string kind)
    {
        var now = time.GetUtcNow();
        lock (_kinds)
        {
            if (_kinds.TryGetValue(kind, out var seen) && now - seen.Last < Window)
            {
                _kinds[kind] = seen with { Skipped = seen.Skipped + 1 };
                return null;
            }
            _kinds[kind] = (now, 0);
            return seen.Skipped;
        }
    }
}
