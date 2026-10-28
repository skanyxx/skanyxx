using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Preferences;

/// <summary>Replaces the organization's defaults; the change is audited in the same save.</summary>
internal sealed class SaveOrganizationPreferencesHandler(AccountsDbContext db, IdentityAudit audit, TimeProvider time)
    : IRequestHandler<SaveOrganizationPreferencesCommand, Outcome<PreferenceSet>>
{
    public async Task<Outcome<PreferenceSet>> Handle(SaveOrganizationPreferencesCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_organization_preferences ("Id", "Json", "UpdatedBy", "UpdatedUtc")
            VALUES ({OrganizationPreference.SingletonId}, CAST({PreferenceJson.Write(command.Preferences)} AS jsonb), {command.ActorId}, {time.GetUtcNow()})
            ON CONFLICT ("Id") DO UPDATE SET "Json" = EXCLUDED."Json", "UpdatedBy" = EXCLUDED."UpdatedBy", "UpdatedUtc" = EXCLUDED."UpdatedUtc"
            """, ct);
        await audit.WriteAsync(AuditActions.OrganizationPreferencesSaved, command.ActorId, null, command.Preferences, ct);
        await transaction.CommitAsync(ct);
        return Outcome<PreferenceSet>.Ok(command.Preferences);
    }
}
