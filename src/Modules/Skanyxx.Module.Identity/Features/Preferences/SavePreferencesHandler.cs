using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Preferences;

/// <summary>Upserts the caller's own row. Only the principal's id reaches here, so nobody can write another person's.</summary>
internal sealed class SavePreferencesHandler(AccountsDbContext db, UserManager<IdentityUser> users, TimeProvider time)
    : IRequestHandler<SavePreferencesCommand, Outcome<PreferencesView>>
{
    public async Task<Outcome<PreferencesView>> Handle(SavePreferencesCommand command, CancellationToken ct)
    {
        if (await users.FindByIdAsync(command.UserId) is null)
            return Outcome<PreferencesView>.NotFound("This account no longer exists.");

        // One statement: two saves at once (two tabs) both land, the later one winning, instead of a key violation.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_user_preferences ("UserId", "Json", "UpdatedUtc")
            VALUES ({command.UserId}, CAST({PreferenceJson.Write(command.Preferences)} AS jsonb), {time.GetUtcNow()})
            ON CONFLICT ("UserId") DO UPDATE SET "Json" = EXCLUDED."Json", "UpdatedUtc" = EXCLUDED."UpdatedUtc"
            """, ct);
        return Outcome<PreferencesView>.Ok(await PreferenceStore.ViewAsync(db, command.UserId, ct));
    }
}
