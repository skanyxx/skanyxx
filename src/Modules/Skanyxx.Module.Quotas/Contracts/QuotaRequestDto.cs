using Skanyxx.Module.Quotas.Data;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas.Contracts;

public sealed record QuotaRequestDto(
    long Id, string UserId, string Name, long Amount, string Reason, string Status, DateTimeOffset CreatedAt,
    string? DecidedBy, DateTimeOffset? DecidedAt)
{
    public static QuotaRequestDto From(QuotaRequest r, OrgSnapshot org) =>
        new(r.Id, r.UserId, org.PersonName(r.UserId), r.Amount, r.Reason, r.Status, r.CreatedAt,
            r.DecidedBy is null ? null : org.PersonName(r.DecidedBy), r.DecidedAt);
}
