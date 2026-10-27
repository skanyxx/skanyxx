using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ChatContext.Cache;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class AnswerCacheStatsHandler(AnswerCache cache) : IRequestHandler<AnswerCacheStatsQuery, Outcome<AnswerCacheStats>>
{
    public async Task<Outcome<AnswerCacheStats>> Handle(AnswerCacheStatsQuery query, CancellationToken ct) =>
        await cache.StatsAsync(query.User.UserId, query.User.IsOwner) is { } stats
            ? Outcome<AnswerCacheStats>.Ok(stats)
            : Outcome<AnswerCacheStats>.Unavailable("The answer cache did not answer.");
}
