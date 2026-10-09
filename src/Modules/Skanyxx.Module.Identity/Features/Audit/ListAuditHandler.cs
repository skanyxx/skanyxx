using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Audit;

/// <summary>Newest first, keyset-paged by id (<see cref="ListAuditQuery.Before"/>), one query.</summary>
internal sealed class ListAuditHandler(AccountsDbContext db) : IRequestHandler<ListAuditQuery, Outcome<IReadOnlyList<AuditEntryDto>>>
{
    public async Task<Outcome<IReadOnlyList<AuditEntryDto>>> Handle(ListAuditQuery query, CancellationToken ct)
    {
        var rows = db.Audit.AsNoTracking();
        if (query.Before is { } before)
            rows = rows.Where(a => a.Id < before);
        if (!string.IsNullOrEmpty(query.Action))
            rows = rows.Where(a => a.Action == query.Action);
        if (!string.IsNullOrEmpty(query.UserId))
            rows = rows.Where(a => a.ActorId == query.UserId || a.TargetId == query.UserId);
        var page = await rows.OrderByDescending(a => a.Id).Take(query.Limit).ToListAsync(ct);
        return Outcome<IReadOnlyList<AuditEntryDto>>.Ok([.. page.Select(a => new AuditEntryDto(a.Id, a.AtUtc, a.Action, a.ActorId, a.TargetId, a.RemoteIp,
            a.Details is null ? null : JsonSerializer.Deserialize<JsonElement>(a.Details)))]);
    }
}
