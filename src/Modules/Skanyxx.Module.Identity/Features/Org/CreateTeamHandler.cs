using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>Into an existing department (nothing is deleted, so it stays). A taken slug is a 409, as for departments.</summary>
internal sealed class CreateTeamHandler(
    AccountsDbContext db, IdentityAudit audit, ClientAddress client, TimeProvider time, ILogger<CreateTeamHandler> logger)
    : IRequestHandler<CreateTeamCommand, Outcome<TeamDto>>
{
    public async Task<Outcome<TeamDto>> Handle(CreateTeamCommand command, CancellationToken ct)
    {
        if (!await db.Departments.AnyAsync(d => d.Slug == command.Department, ct))
            return Outcome<TeamDto>.NotFound($"No department '{command.Department}'.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var created = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_org_teams ("Slug", "Name", "DepartmentSlug", "CreatedBy", "CreatedUtc")
            VALUES ({command.Slug}, {command.Name}, {command.Department}, {command.ActorId}, {time.GetUtcNow()}) ON CONFLICT ("Slug") DO NOTHING
            """, ct);
        if (created == 0)
            return Outcome<TeamDto>.Conflict(null, $"A team with the slug '{command.Slug}' already exists.");
        await audit.WriteAsync(AuditActions.TeamCreated, command.ActorId, command.Slug, new { name = command.Name, department = command.Department }, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Team {Slug} created in department {Department} by {ActorUserId} from {RemoteIp}",
            command.Slug, command.Department, command.ActorId, client.Current);
        return Outcome<TeamDto>.Created(new TeamDto(command.Slug, command.Name, command.Department, []));
    }
}
