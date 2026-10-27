using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class LinkTicketsHandler(ChatContextDbContext db, SessionAccess access, TimeProvider clock)
    : IRequestHandler<LinkTicketsCommand, Outcome<SessionSummary>>
{
    public async Task<Outcome<SessionSummary>> Handle(LinkTicketsCommand command, CancellationToken ct)
    {
        var (session, refusal) = await OwnSession.LoadAsync<SessionSummary>(db, access, command.User, command.SessionId, ct);
        if (session is null)
            return refusal!;

        session.TicketKeys = [.. command.TicketKeys.Distinct()];
        session.RefreshSearchTerms();
        session.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Outcome<SessionSummary>.Ok(SessionSummaries.From(session, command.User.UserId));
    }
}
