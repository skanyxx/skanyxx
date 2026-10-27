using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>Sessions this person may read (D237), best match first, or newest first without text; at most <see cref="Limit"/>.</summary>
internal sealed record SearchSessionsQuery(ChatReader User, SessionFilter Filter) : IRequest<Outcome<IReadOnlyList<SessionSummary>>>
{
    public const int Limit = 50;
}
