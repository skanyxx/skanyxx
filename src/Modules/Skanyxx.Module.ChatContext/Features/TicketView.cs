using Skanyxx.Core.Platform.Tickets;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>
/// Everything known about one ticket (D240): the readable chats linked to it, its pipeline runs, and related tickets
/// with their runs — so a team on SDB-64 finds the chats and runs of SDB-58.
/// </summary>
public sealed record TicketView(
    string TicketKey, IReadOnlyList<SessionSummary> Sessions, IReadOnlyList<TicketRunSummary> Runs, IReadOnlyList<RelatedTicketView> RelatedTickets);
