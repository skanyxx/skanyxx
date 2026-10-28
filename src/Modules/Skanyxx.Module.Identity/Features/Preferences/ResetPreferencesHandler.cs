using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Preferences;

internal sealed class ResetPreferencesHandler(AccountsDbContext db) : IRequestHandler<ResetPreferencesCommand, Outcome<PreferencesView>>
{
    public async Task<Outcome<PreferencesView>> Handle(ResetPreferencesCommand command, CancellationToken ct)
    {
        await db.UserPreferences.Where(p => p.UserId == command.UserId).ExecuteDeleteAsync(ct);
        return Outcome<PreferencesView>.Ok(await PreferenceStore.ViewAsync(db, command.UserId, ct));
    }
}
