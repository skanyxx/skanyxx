using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// Reads at most <see cref="ExportAuditCommand.MaxRows"/> redacted rows, then records who exported what query and how many
/// rows left (the query text is redacted like any detail). Written after the read: the row is the receipt.
/// </summary>
internal sealed class ExportAuditHandler(AccountsDbContext db, IdentityAudit audit) : IRequestHandler<ExportAuditCommand, Outcome<AuditExport>>
{
    public async Task<Outcome<AuditExport>> Handle(ExportAuditCommand command, CancellationToken ct)
    {
        var filter = command.Filter;
        var rows = AuditRows.Matching(db, filter);
        var total = await rows.LongCountAsync(ct);
        var page = await AuditRows.Sorted(rows, command.Ascending).Take(ExportAuditCommand.MaxRows).ToListAsync(ct);
        var ids = page.SelectMany(a => new[] { a.ActorId, a.TargetId }).OfType<string>().Distinct().ToList();
        var emails = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Email != null).ToDictionaryAsync(u => u.Id, u => u.Email!, ct);

        await audit.WriteAsync(AuditActions.Exported, command.ActorId, null, new
        {
            format = command.Format,
            rows = page.Count,
            total,
            from = filter.From,
            to = filter.To,
            query = filter.Q,
            filters = filter.Fields.Select(f => $"{(f.Exclude ? "-" : "")}{f.Field}:{f.Value ?? AuditFields.None}")
        }, ct);
        return Outcome<AuditExport>.Ok(new AuditExport([.. page.Select(AuditEvent.From)], total, emails));
    }
}
