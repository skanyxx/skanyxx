using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ChatContext.Access;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class SearchSessionsHandler(SessionSearch search, SessionAccess access)
    : IRequestHandler<SearchSessionsQuery, Outcome<IReadOnlyList<SessionSummary>>>
{
    public async Task<Outcome<IReadOnlyList<SessionSummary>>> Handle(SearchSessionsQuery query, CancellationToken ct) =>
        Outcome<IReadOnlyList<SessionSummary>>.Ok(
            await search.SearchAsync(await access.ReadableAsync(query.User, ct), query.Filter, SearchSessionsQuery.Limit, ct));
}
