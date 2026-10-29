namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// The turns of people with a limit that are running on this replica (D382): at most <see cref="MaxTurns"/> each, so
/// turns admitted together under the limit can pass it by at most that many turns per replica. A place nobody gave back
/// (a caller that crashed) frees itself after <see cref="Expiry"/>, longer than any turn may run.
/// </summary>
internal sealed class QuotaInFlight(TimeProvider clock)
{
    public const int MaxTurns = 2;
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, List<(object Place, DateTimeOffset Until)>> _turns = [];

    /// <summary>A place for one more turn of <paramref name="userId"/>, or null when they already have <see cref="MaxTurns"/> running.</summary>
    public object? TryEnter(string userId)
    {
        var now = clock.GetUtcNow();
        lock (_turns)
        {
            if (!_turns.TryGetValue(userId, out var running))
                _turns[userId] = running = [];
            running.RemoveAll(t => t.Until <= now);
            if (running.Count >= MaxTurns)
                return null;
            var place = new object();
            running.Add((place, now + Expiry));
            return place;
        }
    }

    public void Leave(string userId, object place)
    {
        lock (_turns)
        {
            if (!_turns.TryGetValue(userId, out var running))
                return;
            running.RemoveAll(t => ReferenceEquals(t.Place, place));
            if (running.Count == 0)
                _turns.Remove(userId);
        }
    }
}
