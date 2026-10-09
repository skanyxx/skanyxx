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
/// Name and department; the slug stays. A move changes which department the members belong to from the next request
/// on: department membership is read through the team, never copied.
/// </summary>
internal sealed class UpdateTeamHandler(AccountsDbContext db, IdentityAudit audit, ClientAddress client, ILogger<UpdateTeamHandler> logger)
    : IRequestHandler<UpdateTeamCommand, Outcome<TeamDto>>
{
    public async Task<Outcome<TeamDto>> Handle(UpdateTeamCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Slug == command.Slug, ct);
        if (team is null)
            return Outcome<TeamDto>.NotFound($"No team '{command.Slug}'.");
        if (!await db.Departments.AnyAsync(d => d.Slug == command.Department, ct))
            return Outcome<TeamDto>.NotFound($"No department '{command.Department}'.");

        var from = team.DepartmentSlug;
        var renamed = team.Name != command.Name;
        team.Name = command.Name;
        team.DepartmentSlug = command.Department;
        await db.SaveChangesAsync(ct);
        if (renamed)
            await audit.WriteAsync(AuditActions.TeamRenamed, command.ActorId, command.Slug, new { name = command.Name }, ct);
        if (from != command.Department)
            await audit.WriteAsync(AuditActions.TeamMoved, command.ActorId, command.Slug, new { from, to = command.Department }, ct);
        await transaction.CommitAsync(ct);

        if (renamed)
            logger.LogWarning("Team {Slug} renamed by {ActorUserId} from {RemoteIp}", command.Slug, command.ActorId, client.Current);
        if (from != command.Department)
            logger.LogWarning("Team {Slug} moved from department {FromDepartment} to {ToDepartment} by {ActorUserId} from {RemoteIp}",
                command.Slug, from, command.Department, command.ActorId, client.Current);
        return Outcome<TeamDto>.Ok(await OrgRows.TeamAsync(db, command.Slug, ct));
    }
}
