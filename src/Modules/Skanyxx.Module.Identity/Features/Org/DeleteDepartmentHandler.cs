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
/// A department with teams goes only with a department to move them to (D351); their members go with them. The row
/// is locked first, so a team created into it concurrently is counted or fails its foreign key.
/// </summary>
internal sealed class DeleteDepartmentHandler(
    AccountsDbContext db, IdentityAudit audit, ClientAddress client, TimeProvider time, ILogger<DeleteDepartmentHandler> logger)
    : IRequestHandler<DeleteDepartmentCommand, Outcome<int>>
{
    public async Task<Outcome<int>> Handle(DeleteDepartmentCommand command, CancellationToken ct)
    {
        if (command.MoveTeamsTo == command.Slug)
            return Outcome<int>.Conflict(0, "Choose a different department to move the teams to.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RetiredSlugs.LockAsync(db, RetiredOrgSlug.Department, command.Slug, ct);
        // Both rows in slug order, so two deletes moving teams into each other's department cannot deadlock.
        var locked = await db.Database.SqlQuery<string>($"""
            SELECT "Slug" AS "Value" FROM identity_org_departments WHERE "Slug" = {command.Slug} OR "Slug" = {command.MoveTeamsTo} ORDER BY "Slug" FOR UPDATE
            """).ToListAsync(ct);
        if (!locked.Contains(command.Slug))
            return Outcome<int>.NotFound($"No department '{command.Slug}'.");

        var teams = await db.Database.SqlQuery<string>($"""
            SELECT "Slug" AS "Value" FROM identity_org_teams WHERE "DepartmentSlug" = {command.Slug} ORDER BY "Slug" FOR UPDATE
            """).ToListAsync(ct);
        if (teams.Count > 0 && command.MoveTeamsTo is null)
            return Outcome<int>.Conflict(teams.Count,
                $"The department has {teams.Count} team(s). Choose a department to move them to, or delete them first.", "has_teams");
        if (command.MoveTeamsTo is { } target)
        {
            if (!locked.Contains(target))
                return Outcome<int>.NotFound($"No department '{target}'.");
            await db.Teams.Where(t => t.DepartmentSlug == command.Slug)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DepartmentSlug, target), ct);
            foreach (var team in teams)
                await audit.WriteAsync(AuditActions.TeamMoved, command.ActorId, team, new { from = command.Slug, to = target }, ct);
        }

        await db.Departments.Where(d => d.Slug == command.Slug).ExecuteDeleteAsync(ct);
        await RetiredSlugs.RetireAsync(db, RetiredOrgSlug.Department, command.Slug, command.ActorId, time.GetUtcNow(), ct);
        await audit.WriteAsync(AuditActions.DepartmentDeleted, command.ActorId, command.Slug,
            new { teams = teams.Count, movedTo = command.MoveTeamsTo }, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Department {Slug} deleted by {ActorUserId} from {RemoteIp}; {Teams} team(s) moved to {MoveTeamsTo}",
            command.Slug, command.ActorId, client.Current, teams.Count, command.MoveTeamsTo);
        return Outcome<int>.Ok(teams.Count);
    }
}
