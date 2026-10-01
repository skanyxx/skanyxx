using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Org;

/// <summary>
/// Adds an existing, enabled person to a team, or removes them. Idempotent both ways; only a real change is logged.
/// It takes effect on the person's next request: memory asks <see cref="IOrgMembership"/> on every request.
/// </summary>
internal sealed class SetTeamMemberHandler(AccountsDbContext db, ClientAddress client, TimeProvider time, ILogger<SetTeamMemberHandler> logger)
    : IRequestHandler<SetTeamMemberCommand, Outcome<TeamDto>>
{
    public async Task<Outcome<TeamDto>> Handle(SetTeamMemberCommand command, CancellationToken ct)
    {
        if (!await db.Teams.AnyAsync(t => t.Slug == command.Team, ct))
            return Outcome<TeamDto>.NotFound($"No team '{command.Team}'.");

        if (command.Member && await AddAsync(command, ct) is { } refusal)
            return refusal;
        if (!command.Member)
            await RemoveAsync(command, ct);

        return Outcome<TeamDto>.Ok(await OrgRows.TeamAsync(db, command.Team, ct));
    }

    /// <summary>
    /// Null when the person is a member now. Under the account lock that disable/enable takes, so a disable cannot commit
    /// between the check and the insert and leave a disabled person in a team.
    /// </summary>
    private async Task<Outcome<TeamDto>?> AddAsync(SetTeamMemberCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var email = await db.Users.Where(u => u.Id == command.UserId).Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct);
        if (email is null)
            return Outcome<TeamDto>.NotFound("No person has that id.");

        await AccountLock.AcquireAsync(db, email, ct);
        var lockoutEnd = await db.Users.Where(u => u.Id == command.UserId).Select(u => u.LockoutEnd).SingleAsync(ct);
        if (AccountStatus.IsDisabled(lockoutEnd))
            return Outcome<TeamDto>.Conflict(null, "The account is disabled; enable it before adding it to a team.");

        var added = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_org_team_members ("TeamSlug", "UserId", "AddedBy", "AddedUtc")
            VALUES ({command.Team}, {command.UserId}, {command.ActorId}, {time.GetUtcNow()}) ON CONFLICT ("TeamSlug", "UserId") DO NOTHING
            """, ct);
        await transaction.CommitAsync(ct);
        if (added > 0)
            logger.LogWarning("{UserId} added to team {Team} by {ActorUserId} from {RemoteIp}", command.UserId, command.Team, command.ActorId, client.Current);
        return null;
    }

    private async Task RemoveAsync(SetTeamMemberCommand command, CancellationToken ct)
    {
        var removed = await db.TeamMembers.Where(m => m.TeamSlug == command.Team && m.UserId == command.UserId).ExecuteDeleteAsync(ct);
        if (removed > 0)
            logger.LogWarning("{UserId} removed from team {Team} by {ActorUserId} from {RemoteIp}", command.UserId, command.Team, command.ActorId, client.Current);
    }
}
