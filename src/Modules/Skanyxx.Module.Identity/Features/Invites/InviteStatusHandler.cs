using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Invites;

internal sealed class InviteStatusHandler(
    AccountsDbContext db, ClientAddress client, TimeProvider time, ILogger<InviteStatusHandler> logger)
    : IRequestHandler<InviteStatusQuery, Outcome<InviteDetails>>
{
    /// <summary>The one answer for unknown, used, revoked and expired: which of them it is would help nobody but a guesser.</summary>
    public const string Invalid = "This invite link is invalid or has expired. Ask the owner for a new one.";

    public async Task<Outcome<InviteDetails>> Handle(InviteStatusQuery query, CancellationToken ct)
    {
        var hash = InviteTokens.Hash(query.Token);
        var now = time.GetUtcNow();
        var invite = await db.Invites.AsNoTracking()
            .Where(i => i.TokenHash == hash && i.AcceptedUtc == null && i.RevokedUtc == null && i.ExpiresUtc > now)
            .Select(i => new InviteDetails(i.Email, i.Roles, i.ExpiresUtc))
            .SingleOrDefaultAsync(ct);
        if (invite is not null)
            return Outcome<InviteDetails>.Ok(invite);

        // Never the token: this line is where guessing shows, next to the 429s.
        logger.LogWarning("Invite lookup refused from {RemoteIp}: invalid", client.Current);
        return Outcome<InviteDetails>.NotFound(Invalid);
    }
}
