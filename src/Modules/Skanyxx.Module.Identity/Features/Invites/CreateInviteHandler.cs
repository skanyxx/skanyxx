using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Invites;

/// <summary>
/// Under the email's account lock (the one sign-in and accept take), so an invite cannot race an accept for the same
/// email or a second invite: an existing account is refused, the open invite for the email is revoked, a new one is written.
/// </summary>
internal sealed class CreateInviteHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, IOptions<IdentityModuleOptions> options, InviteLinks links,
    ClientAddress client, TimeProvider time, ILogger<CreateInviteHandler> logger)
    : IRequestHandler<CreateInviteCommand, Outcome<InviteIssued>>
{
    public async Task<Outcome<InviteIssued>> Handle(CreateInviteCommand command, CancellationToken ct)
    {
        // Before any write: an older open invite must not be revoked for a new one that cannot be sent.
        if (!links.CanBuild)
            return Outcome<InviteIssued>.Conflict(null, InviteLinks.NotConfigured);

        var email = users.NormalizeEmail(command.Email);
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLock.AcquireAsync(db, email, ct);
        if (await users.FindByEmailAsync(command.Email) is not null)
            return Outcome<InviteIssued>.Conflict(null, "An account with this email already exists.");

        var revoked = await db.Invites.Where(i => i.NormalizedEmail == email && i.AcceptedUtc == null && i.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedUtc, now), ct);
        var token = InviteTokens.New();
        var invite = new Invite
        {
            Id = Guid.NewGuid().ToString(),
            Email = command.Email,
            NormalizedEmail = email,
            Roles = [.. command.Roles.Distinct().Order()],
            TokenHash = InviteTokens.Hash(token),
            CreatedBy = command.ActorId,
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(options.Value.InviteDays)
        };
        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Invite {InviteId} created by {ActorUserId} from {RemoteIp} with roles {Roles}, expires {ExpiresUtc}; older invites revoked: {Revoked}",
            invite.Id, command.ActorId, client.Current, invite.Roles, invite.ExpiresUtc, revoked);
        return Outcome<InviteIssued>.Created(new InviteIssued(invite.Id, links.For(token), invite.ExpiresUtc));
    }
}
