using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>
/// A team with members goes only with a team to reassign them to (D351). The row is locked first, so a member added
/// concurrently either commits before the count or waits and then fails its foreign key; nobody is dropped silently.
/// A team an Entra group maps to is refused: the next Microsoft sign-in would look for it.
/// </summary>
internal sealed class DeleteTeamHandler(
    AccountsDbContext db, IdentityAudit audit, ClientAddress client, TimeProvider time, ILogger<DeleteTeamHandler> logger)
    : IRequestHandler<DeleteTeamCommand, Outcome<int>>
{
    public async Task<Outcome<int>> Handle(DeleteTeamCommand command, CancellationToken ct)
    {
        if (command.ReassignTo == command.Slug)
            return Outcome<int>.Conflict(0, "Choose a different team to move the members to.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var department = (await db.Database.SqlQuery<string>(
            $"""SELECT "DepartmentSlug" AS "Value" FROM identity_org_teams WHERE "Slug" = {command.Slug} FOR UPDATE""").ToListAsync(ct)).SingleOrDefault();
        if (department is null)
            return Outcome<int>.NotFound($"No team '{command.Slug}'.");
        if (await db.EntraGroups.AnyAsync(g => g.Teams.Contains(command.Slug), ct))
            return Outcome<int>.Conflict(0, "A Microsoft (Entra) group maps to this team. Remove it from the group mapping first.");

        var members = await db.TeamMembers.Where(m => m.TeamSlug == command.Slug).Select(m => m.UserId).ToListAsync(ct);
        if (members.Count > 0 && command.ReassignTo is null)
            return Outcome<int>.Conflict(members.Count,
                $"The team has {members.Count} member(s). Choose a team to move them to, or remove them first.", "has_members");
        if (command.ReassignTo is { } target)
        {
            var exists = (await db.Database.SqlQuery<string>(
                $"""SELECT "Slug" AS "Value" FROM identity_org_teams WHERE "Slug" = {target} FOR SHARE""").ToListAsync(ct)).Count > 0;
            if (!exists)
                return Outcome<int>.NotFound($"No team '{target}'.");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity_org_team_members ("TeamSlug", "UserId", "AddedBy", "AddedUtc")
                SELECT {target}, "UserId", {command.ActorId}, {time.GetUtcNow()} FROM identity_org_team_members WHERE "TeamSlug" = {command.Slug}
                ON CONFLICT ("TeamSlug", "UserId") DO NOTHING
                """, ct);
        }

        await db.TeamMembers.Where(m => m.TeamSlug == command.Slug).ExecuteDeleteAsync(ct);
        await db.Teams.Where(t => t.Slug == command.Slug).ExecuteDeleteAsync(ct);
        await RetiredSlugs.RetireAsync(db, RetiredOrgSlug.Team, command.Slug, command.ActorId, time.GetUtcNow(), ct);
        await audit.WriteAsync(AuditActions.TeamDeleted, command.ActorId, command.Slug,
            new { department, members = members.Count, reassignedTo = command.ReassignTo }, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Team {Slug} deleted by {ActorUserId} from {RemoteIp}; {Members} member(s) moved to {ReassignTo}",
            command.Slug, command.ActorId, client.Current, members.Count, command.ReassignTo);
        return Outcome<int>.Ok(members.Count);
    }
}
