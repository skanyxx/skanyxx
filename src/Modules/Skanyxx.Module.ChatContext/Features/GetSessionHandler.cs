using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class GetSessionHandler(ChatContextDbContext db, SessionAccess access) : IRequestHandler<GetSessionQuery, Outcome<SessionSummary>>
{
    public async Task<Outcome<SessionSummary>> Handle(GetSessionQuery query, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([query.SessionId], ct);
        return session is not null && SessionAccess.CanRead(await access.ReadableAsync(query.User, ct), session)
            ? Outcome<SessionSummary>.Ok(SessionSummaries.From(session, query.User.UserId))
            : Outcome<SessionSummary>.NotFound("No such session.");
    }
}
