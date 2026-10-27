using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class ShareSessionHandler(ChatContextDbContext db, SessionAccess access, ILogger<ShareSessionHandler> logger)
    : IRequestHandler<ShareSessionCommand, Outcome<SessionSummary>>
{
    public async Task<Outcome<SessionSummary>> Handle(ShareSessionCommand command, CancellationToken ct)
    {
        var (session, refusal) = await OwnSession.LoadAsync<SessionSummary>(db, access, command.User, command.SessionId, ct);
        if (session is null)
            return refusal!;
        if (!await access.CanShareToAsync(command.User, command.Scope, ct))
            return Outcome<SessionSummary>.Forbidden($"You may not share into '{command.Scope}'.");

        logger.LogInformation("{UserId} shares chat session {SessionId}: {From} -> {To}", command.User.UserId, session.Id, session.Scope, command.Scope);
        session.Scope = command.Scope;
        await db.SaveChangesAsync(ct);
        return Outcome<SessionSummary>.Ok(SessionSummaries.From(session, command.User.UserId!));
    }
}
