using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Preferences;

internal sealed class GetPreferencesHandler(AccountsDbContext db) : IRequestHandler<GetPreferencesQuery, Outcome<PreferencesView>>
{
    public async Task<Outcome<PreferencesView>> Handle(GetPreferencesQuery query, CancellationToken ct) =>
        Outcome<PreferencesView>.Ok(await PreferenceStore.ViewAsync(db, query.UserId, ct));
}
