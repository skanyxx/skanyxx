using MediatR;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Library;

/// <summary>The agents' search path (published only) over <c>company</c> alone.</summary>
internal sealed class CompanyCardsHandler(CardSearch search) : IRequestHandler<CompanyCardsQuery, IReadOnlyList<LibraryEntry>>
{
    public async Task<IReadOnlyList<LibraryEntry>> Handle(CompanyCardsQuery query, CancellationToken ct) =>
        [.. (await search.SearchAsync(query.Text, new SearchScopes([Scope.CompanyName], AllTeamsAndDepartments: false), query.Limit, ct))
            .Select(c => new LibraryEntry(c.Scope, c.Key, c.Version, c.Type, c.What, c.Why, c.Who, c.UpdatedAt, "published"))];
}
