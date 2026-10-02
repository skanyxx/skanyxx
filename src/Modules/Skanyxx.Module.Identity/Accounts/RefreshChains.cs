using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>Single-use refresh tokens: each refresh swaps the chain's current token id in one conditional UPDATE.</summary>
internal sealed class RefreshChains(AccountsDbContext db, IOptions<IdentityModuleOptions> options)
{
    public async Task<RefreshChain> StartAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        await db.RefreshSessions.Where(s => s.UserId == userId && s.ExpiresUtc <= now).ExecuteDeleteAsync(ct);
        var session = new RefreshSession { Id = NewId(), UserId = userId, TokenId = NewId(), ExpiresUtc = now.AddDays(options.Value.SessionDays) };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return new RefreshChain(session.Id, session.TokenId, session.ExpiresUtc);
    }

    /// <summary>
    /// The chain with a fresh token id, or null: the chain is gone (signed out), past its cap, or the presented token
    /// was already used. Reuse means a copy is in someone else's hands, so the whole chain is revoked.
    /// </summary>
    public async Task<RefreshChain?> AdvanceAsync(string userId, RefreshChain presented, DateTimeOffset now, CancellationToken ct)
    {
        var next = NewId();
        var advanced = await db.RefreshSessions
            .Where(s => s.Id == presented.Id && s.UserId == userId && s.TokenId == presented.TokenId && s.ExpiresUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TokenId, next), ct);
        if (advanced == 1)
            return presented with { TokenId = next };

        await db.RefreshSessions.Where(s => s.Id == presented.Id).ExecuteDeleteAsync(ct);
        return null;
    }

    public Task RevokeAllAsync(string userId, CancellationToken ct) =>
        db.RefreshSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);

    private static string NewId() => Guid.NewGuid().ToString();
}
