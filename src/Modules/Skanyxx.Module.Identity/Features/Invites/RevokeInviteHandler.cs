using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Invites;

/// <summary>One conditional UPDATE: it and an accept of the same invite serialize on the row, and only one wins.</summary>
internal sealed class RevokeInviteHandler(AccountsDbContext db, ClientAddress client, TimeProvider time, ILogger<RevokeInviteHandler> logger)
    : IRequestHandler<RevokeInviteCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(RevokeInviteCommand command, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var revoked = await db.Invites
            .Where(i => i.Id == command.InviteId && i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedUtc, now), ct);
        if (revoked == 0)
            return Outcome<bool>.NotFound("No pending invite has that id.");

        logger.LogWarning("Invite {InviteId} revoked by {ActorUserId} from {RemoteIp}", command.InviteId, command.ActorId, client.Current);
        return Outcome<bool>.Ok(true);
    }
}
