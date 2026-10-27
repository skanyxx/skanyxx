using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// A ticket pipeline run reached its end (succeeded or failed): published by the tickets module, kept by memory as a
/// card in the run owner's personal scope (requirements.md §3.2). Nothing listens when memory is off.
/// </summary>
/// <param name="Outcome">A short account of how it ended (the last stage's output, cut); never the whole run.</param>
/// <param name="RelatedTickets">Other ticket keys the ticket mentions.</param>
public sealed record TicketRunFinished(
    Guid RunId, string OwnerUserId, string TicketKey, string TicketTitle, string? TicketUrl, string PipelineName,
    bool Succeeded, string Outcome, IReadOnlyList<string> RelatedTickets, IReadOnlyList<string> Labels) : INotification;
