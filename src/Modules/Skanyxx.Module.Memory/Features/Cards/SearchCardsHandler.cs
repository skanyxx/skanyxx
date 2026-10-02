using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class SearchCardsHandler(CardSearch search, AccessPolicy access, IOptions<MemoryOptions> options)
    : IRequestHandler<SearchCardsQuery, IReadOnlyList<CardHit>>
{
    public async Task<IReadOnlyList<CardHit>> Handle(SearchCardsQuery query, CancellationToken ct)
    {
        var scopes = await access.SearchScopesAsync(query.Caller, ct);
        return await search.SearchAsync(query.Query, scopes, options.Value.SearchTopK, ct);
    }
}
