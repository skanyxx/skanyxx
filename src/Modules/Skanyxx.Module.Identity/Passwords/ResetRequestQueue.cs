using System.Threading.Channels;

namespace Skanyxx.Module.Identity.Passwords;

/// <summary>
/// In-process, bounded: the request answers before anything is looked up or sent, so neither its timing nor its body
/// says whether the account exists (D156). A full queue drops the request (the person asks again); a restart loses what
/// was waiting, and that is the same answer.
/// </summary>
internal sealed class ResetRequestQueue
{
    public const int Capacity = 256;

    private readonly Channel<ResetRequest> _channel = Channel.CreateBounded<ResetRequest>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public bool TryEnqueue(ResetRequest request) => _channel.Writer.TryWrite(request);

    public ChannelReader<ResetRequest> Reader => _channel.Reader;
}
