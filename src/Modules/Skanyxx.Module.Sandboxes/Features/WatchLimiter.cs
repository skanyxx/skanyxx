using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Sandboxes.Features;

/// <summary>Open watches per user and in total: each pins a connection, an AX stream and a Pub/Sub subscription inside AX.</summary>
internal sealed class WatchLimiter(IOptions<SandboxesOptions> options)
{
    private readonly Dictionary<string, int> _perUser = [];
    private int _total;

    public bool TryEnter(string user)
    {
        lock (_perUser)
        {
            var mine = _perUser.GetValueOrDefault(user);
            if (mine >= options.Value.MaxWatchesPerUser || _total >= options.Value.MaxWatches)
                return false;
            _perUser[user] = mine + 1;
            _total++;
            return true;
        }
    }

    public void Exit(string user)
    {
        lock (_perUser)
        {
            _total--;
            var mine = _perUser[user] - 1;
            if (mine == 0)
                _perUser.Remove(user);
            else
                _perUser[user] = mine;
        }
    }
}
