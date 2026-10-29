using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Quotas.Data;

/// <summary>
/// The quotas' tables. Usage is added with one atomic upsert per turn, so concurrent turns of one person never lose a
/// token; a request is decided with one conditional update, so two approvers cannot both decide it.
/// </summary>
internal sealed class QuotaStore(QuotaDbContext db)
{
    public async Task AddUsageAsync(DateOnly month, ChatQuotaUsage turn, CancellationToken ct)
    {
        var (prompt, completion, estimated, estimatedTurns) = turn.Estimated
            ? (0L, 0L, turn.Usage.Total, 1L)
            // A thinking model's total also holds thought and tool-use tokens: those count, as output.
            : (turn.Usage.Prompt, Math.Max(turn.Usage.Completion, turn.Usage.Total - turn.Usage.Prompt), 0L, 0L);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO quota_usage (month, user_id, model, prompt_tokens, completion_tokens, estimated_tokens, turns, estimated_turns)
            VALUES ({month}, {turn.UserId}, {turn.Model}, {prompt}, {completion}, {estimated}, 1, {estimatedTurns})
            ON CONFLICT (month, user_id, model) DO UPDATE SET
                prompt_tokens = quota_usage.prompt_tokens + EXCLUDED.prompt_tokens,
                completion_tokens = quota_usage.completion_tokens + EXCLUDED.completion_tokens,
                estimated_tokens = quota_usage.estimated_tokens + EXCLUDED.estimated_tokens,
                turns = quota_usage.turns + 1,
                estimated_turns = quota_usage.estimated_turns + EXCLUDED.estimated_turns
            """, ct);
    }

    /// <summary><paramref name="users"/> null = everyone.</summary>
    public async Task<IReadOnlyList<QuotaUsage>> UsageAsync(DateOnly month, IReadOnlyCollection<string>? users, CancellationToken ct)
    {
        var rows = db.Usage.AsNoTracking().Where(u => u.Month == month);
        if (users is not null)
            rows = rows.Where(u => users.Contains(u.UserId));
        return await rows.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<QuotaLimit>> LimitsAsync(CancellationToken ct) =>
        await db.Limits.AsNoTracking().ToListAsync(ct);

    /// <summary>Sets (or with null clears) a limit; answers the previous value.</summary>
    public async Task<long?> SetLimitAsync(string kind, string id, long? tokens, string actor, DateTimeOffset now, CancellationToken ct)
    {
        var limit = await db.Limits.FindAsync([kind, id], ct);
        var previous = limit?.MonthlyTokens;
        if (tokens is null)
        {
            if (limit is not null)
                db.Limits.Remove(limit);
        }
        else
        {
            if (limit is null)
                db.Limits.Add(limit = new QuotaLimit { Kind = kind, ScopeId = id });
            (limit.MonthlyTokens, limit.SetBy, limit.SetAt) = (tokens.Value, actor, now);
        }
        await db.SaveChangesAsync(ct);
        return previous;
    }

    public async Task<IReadOnlyList<QuotaPrice>> PricesAsync(CancellationToken ct) =>
        await db.Prices.AsNoTracking().OrderBy(p => p.Model).ToListAsync(ct);

    public async Task<decimal?> SetPriceAsync(string model, decimal? price, string actor, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Prices.FindAsync([model], ct);
        var previous = row?.PricePer1K;
        if (price is null)
        {
            if (row is not null)
                db.Prices.Remove(row);
        }
        else
        {
            if (row is null)
                db.Prices.Add(row = new QuotaPrice { Model = model });
            (row.PricePer1K, row.SetBy, row.SetAt) = (price.Value, actor, now);
        }
        await db.SaveChangesAsync(ct);
        return previous;
    }

    /// <summary>Tokens approved requests added, per person, for <paramref name="month"/>.</summary>
    public async Task<IReadOnlyDictionary<string, long>> ExtrasAsync(DateOnly month, IReadOnlyCollection<string>? users, CancellationToken ct)
    {
        var rows = db.Requests.AsNoTracking().Where(r => r.Status == RequestStatuses.Approved && r.Month == month);
        if (users is not null)
            rows = rows.Where(r => users.Contains(r.UserId));
        return await rows.GroupBy(r => r.UserId).Select(g => new { g.Key, Sum = g.Sum(r => r.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
    }

    /// <summary>Null when the person already has an open request (the database's one-pending index decides).</summary>
    public async Task<QuotaRequest?> AddRequestAsync(string userId, long amount, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var request = new QuotaRequest { UserId = userId, Amount = amount, Reason = reason, CreatedAt = now };
        db.Requests.Add(request);
        try
        {
            await db.SaveChangesAsync(ct);
            return request;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Requests.Entry(request).State = EntityState.Detached;
            return null;
        }
    }

    public Task<QuotaRequest?> RequestAsync(long id, CancellationToken ct) =>
        db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>Newest first; <paramref name="users"/> null = everyone.</summary>
    public async Task<IReadOnlyList<QuotaRequest>> RequestsAsync(IReadOnlyCollection<string>? users, int take, CancellationToken ct)
    {
        var rows = db.Requests.AsNoTracking();
        if (users is not null)
            rows = rows.Where(r => users.Contains(r.UserId));
        return await rows.OrderByDescending(r => r.Status == RequestStatuses.Pending).ThenByDescending(r => r.Id).Take(take).ToListAsync(ct);
    }

    /// <summary>False when it was no longer pending (someone else decided it first).</summary>
    public async Task<bool> DecideAsync(long id, string status, string actor, DateTimeOffset now, DateOnly month, CancellationToken ct) =>
        await db.Requests.Where(r => r.Id == id && r.Status == RequestStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.DecidedBy, actor)
                .SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.Month, month), ct) == 1;
}
