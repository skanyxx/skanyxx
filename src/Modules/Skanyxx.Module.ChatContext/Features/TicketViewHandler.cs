using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Tickets;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>
/// Related tickets come from readable sessions only: those linked to this ticket give their other links and their
/// keywords, and readable sessions matching those keywords give their tickets. Runs come from the tickets module when it
/// is loaded.
/// </summary>
internal sealed class TicketViewHandler(SessionSearch search, SessionAccess access, IEnumerable<ITicketRuns> runs)
    : IRequestHandler<TicketViewQuery, Outcome<TicketView>>
{
    public async Task<Outcome<TicketView>> Handle(TicketViewQuery query, CancellationToken ct)
    {
        var readable = await access.ReadableAsync(query.User, ct);
        var sessions = await search.SearchAsync(readable, new SessionFilter(Ticket: query.TicketKey), TicketViewQuery.MaxSessions, ct);
        var keywords = sessions.SelectMany(s => s.Keywords).GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).Take(SessionSearch.MaxTerms).ToList();
        var related = await search.RelatedTicketsAsync(readable, query.TicketKey, keywords, TicketViewQuery.MaxRelated, ct);

        var allRuns = new List<TicketRunSummary>();
        foreach (var source in runs)
            allRuns.AddRange(await source.ForTicketsAsync([query.TicketKey, .. related.Select(r => r.TicketKey)], TicketViewQuery.MaxRuns, ct));
        var byTicket = allRuns.ToLookup(r => r.TicketKey);

        return Outcome<TicketView>.Ok(new TicketView(query.TicketKey, sessions, [.. byTicket[query.TicketKey]],
            [.. related.Select(r => new RelatedTicketView(r.TicketKey, r.Sessions, r.Score, [.. byTicket[r.TicketKey]]))]));
    }
}
