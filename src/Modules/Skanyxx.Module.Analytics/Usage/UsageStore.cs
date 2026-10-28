using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Module.Analytics.Data;

namespace Skanyxx.Module.Analytics.Usage;

/// <summary>
/// The daily rollup. A turn is added with one atomic upsert (parameterized), so concurrent turns of one person never lose
/// a count. Reads are bounded by a day range the caller already checked (<see cref="Reports.DateRange"/>).
/// </summary>
internal sealed class UsageStore(AnalyticsDbContext db)
{
    public async Task AddAsync(ChatTurnUsage turn, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(turn.At.UtcDateTime);
        var chats = turn.NewChat ? 1L : 0L;
        var unreported = turn.Usage.Reported || turn.Cached || turn.Failed ? 0L : 1L;
        var hits = turn.Cached ? 1L : 0L;
        var errors = turn.Failed ? 1L : 0L;
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO analytics_usage_daily
                (day, user_id, agent, model, requests, chats, tokens_in, tokens_out, unreported, cache_hits, tokens_saved, errors)
            VALUES ({day}, {turn.UserId}, {turn.AgentRef}, {turn.Model}, 1, {chats}, {turn.Usage.InputTokens}, {turn.Usage.OutputTokens},
                {unreported}, {hits}, {turn.TokensSaved}, {errors})
            ON CONFLICT (day, user_id, agent, model) DO UPDATE SET
                requests = analytics_usage_daily.requests + 1,
                chats = analytics_usage_daily.chats + EXCLUDED.chats,
                tokens_in = analytics_usage_daily.tokens_in + EXCLUDED.tokens_in,
                tokens_out = analytics_usage_daily.tokens_out + EXCLUDED.tokens_out,
                unreported = analytics_usage_daily.unreported + EXCLUDED.unreported,
                cache_hits = analytics_usage_daily.cache_hits + EXCLUDED.cache_hits,
                tokens_saved = analytics_usage_daily.tokens_saved + EXCLUDED.tokens_saved,
                errors = analytics_usage_daily.errors + EXCLUDED.errors
            """, ct);
    }

    /// <summary>Rows from <paramref name="from"/> to <paramref name="to"/>, inclusive; <paramref name="users"/> null = everyone.</summary>
    public async Task<IReadOnlyList<UsageDay>> ReadAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string>? users, CancellationToken ct)
    {
        var rows = db.UsageDays.AsNoTracking().Where(u => u.Day >= from && u.Day <= to);
        if (users is not null)
            rows = rows.Where(u => users.Contains(u.UserId));
        return await rows.ToListAsync(ct);
    }
}
