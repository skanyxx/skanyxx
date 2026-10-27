using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Cache;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class ForgetSessionHandler(ChatContextDbContext db, SessionAccess access, AnswerCache cache)
    : IRequestHandler<ForgetSessionCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(ForgetSessionCommand command, CancellationToken ct)
    {
        var (session, refusal) = await OwnSession.LoadAsync<bool>(db, access, command.User, command.SessionId, ct);
        if (session is null)
            return refusal!;

        db.Sessions.Remove(session);
        await db.SaveChangesAsync(ct);
        // A cached answer may point at this conversation.
        if (cache.On)
            await cache.ClearAsync(command.User.UserId!);
        return Outcome<bool>.Ok(true);
    }
}
