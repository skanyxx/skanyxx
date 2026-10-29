using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>
/// A turn ready to go to kagent: the agent, the caller's session (<paramref name="IsNew"/> when this turn created it),
/// and the <paramref name="Prompt"/> — related work, related cards and attached files in front of the message. A
/// non-null <c>Prepared.Cached</c> answers it without kagent. <paramref name="Admission"/> is the quota's: disposed when
/// the turn is over (counting it does that too).
/// </summary>
internal sealed record TurnStart(
    Agent Agent, string AgentRef, string SessionId, bool IsNew, string Title,
    ChatPreparation Prepared, MemoryContext Memory, string Prompt, IReadOnlyList<A2AFilePart> Images, ChatQuotaAdmission Admission)
{
    private int _recorded;

    /// <summary>True the first time only: a turn's usage is recorded once, whichever path reaches it first.</summary>
    public bool FirstRecord() => Interlocked.Exchange(ref _recorded, 1) == 0;
}
