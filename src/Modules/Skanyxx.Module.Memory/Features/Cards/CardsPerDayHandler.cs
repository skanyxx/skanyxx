using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>New cards per UTC day for the Dashboard: a count by <c>created_at</c>, nothing about the cards themselves.</summary>
internal sealed class CardsPerDayHandler(MemoryDbContext db) : IRequestHandler<CardsPerDayQuery, IReadOnlyList<DayCount>>
{
    public async Task<IReadOnlyList<DayCount>> Handle(CardsPerDayQuery query, CancellationToken ct)
    {
        var from = query.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = query.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var days = await db.Cards.AsNoTracking()
            .Where(c => c.CreatedAt >= from && c.CreatedAt < to)
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);
        return [.. days.Select(d => new DayCount(DateOnly.FromDateTime(d.Day), d.Count)).OrderBy(d => d.Day)];
    }
}
