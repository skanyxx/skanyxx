using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Library;

/// <summary>
/// The library list (D009, D048): exactly the scopes <see cref="AccessPolicy.SearchScopesAsync"/> gives this person,
/// narrowed to one scope only if <see cref="AccessPolicy.CanReadAsync"/> allows it.
/// </summary>
internal sealed class BrowseLibraryHandler(CardSearch search, AccessPolicy access)
    : IRequestHandler<BrowseLibraryQuery, Outcome<LibraryShelf>>
{
    public async Task<Outcome<LibraryShelf>> Handle(BrowseLibraryQuery query, CancellationToken ct)
    {
        var caller = MemoryCaller.For(query.User);
        var readable = await access.SearchScopesAsync(caller, ct);
        var scopes = readable.AllTeamsAndDepartments
            ? [.. readable.Exact, .. (await search.OrgScopesWithCardsAsync(ct)).Except(readable.Exact)]
            : readable.Exact;

        var within = readable;
        if (!string.IsNullOrEmpty(query.Scope))
        {
            var scope = Scope.Parse(query.Scope);
            if (!await access.CanReadAsync(caller, scope, ct))
                return Outcome<LibraryShelf>.Forbidden($"'{scope}' is not readable by this caller.");
            within = new SearchScopes([scope.ToString()], AllTeamsAndDepartments: false);
        }

        var cards = await search.LibraryAsync(query.Text, within, LibraryShelf.Limit, ct);
        return Outcome<LibraryShelf>.Ok(new LibraryShelf(scopes, cards));
    }
}
