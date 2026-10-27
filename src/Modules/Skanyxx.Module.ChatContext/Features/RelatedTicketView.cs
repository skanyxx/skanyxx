using Skanyxx.Core.Platform.Tickets;

namespace Skanyxx.Module.ChatContext.Features;

/// <param name="Sessions">Readable sessions linked to it that matched.</param>
public sealed record RelatedTicketView(string TicketKey, long Sessions, double Score, IReadOnlyList<TicketRunSummary> Runs);
