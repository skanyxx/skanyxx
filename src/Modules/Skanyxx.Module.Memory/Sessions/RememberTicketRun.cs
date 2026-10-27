using MediatR;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Sessions;

/// <summary>requirements.md §3.2: a finished run becomes a card in its owner's personal scope, liftable like any card.</summary>
internal sealed class RememberTicketRun(ICardExtractor extractor) : INotificationHandler<TicketRunFinished>
{
    public Task Handle(TicketRunFinished notification, CancellationToken ct) => extractor.TicketRunAsync(notification, ct);
}
