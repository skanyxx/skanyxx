using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed record TicketViewQuery(ChatReader User, string TicketKey) : IRequest<Outcome<TicketView>>
{
    public const int MaxSessions = 50;
    public const int MaxRelated = 10;
    public const int MaxRuns = 50;
}
