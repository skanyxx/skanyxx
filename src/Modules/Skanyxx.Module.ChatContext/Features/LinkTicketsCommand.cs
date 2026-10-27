using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>Replaces the session's ticket links (D240). Its owner only.</summary>
internal sealed record LinkTicketsCommand(ChatReader User, string SessionId, IReadOnlyList<string> TicketKeys) : IRequest<Outcome<SessionSummary>>;
