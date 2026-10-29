using System.Globalization;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Contracts;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// Team and department limits are the owner's. A person's limit: the owner's for anyone; a supervisor's only for the
/// members of their own teams (never themselves, the owner or another supervisor), and never so that the person's effective
/// limit, approved extras included, ends above their team or department limit (D333, D338).
/// Audited with names and numbers only.
/// </summary>
internal sealed class SetLimitHandler(OrgReader orgs, QuotaLedger ledger, QuotaStore store, IMediator mediator, TimeProvider clock)
    : IRequestHandler<SetLimitCommand, Outcome<LimitResult>>
{
    public async Task<Outcome<LimitResult>> Handle(SetLimitCommand command, CancellationToken ct)
    {
        var (viewer, kind, id) = (command.Viewer, command.Kind, command.Id);
        var org = await orgs.ReadAsync(ct);
        if (kind != QuotaScopes.User && !viewer.IsOwner)
            return Outcome<LimitResult>.Forbidden("Only the owner sets team and department quotas.");
        if (kind == QuotaScopes.User && !viewer.MayActFor(id, org))
            return Outcome<LimitResult>.Forbidden("You can set quotas only for the members of your own teams.");
        if (!org.Exists(kind, id))
            return Outcome<LimitResult>.NotFound($"No such {kind}.");

        if (!viewer.IsOwner)
        {
            var (teams, departments) = (org.TeamsOf(id).Select(t => t.Slug).ToList(), org.DepartmentsOf(id));
            var book = await ledger.BookAsync(QuotaMonth.Of(clock.GetUtcNow()), [id], ct);
            var applicable = book.ApplicableTo(id, teams, departments);
            var extra = book.Extras.GetValueOrDefault(id);
            var cap = QuotaResolver.SupervisorCap(applicable);
            var current = QuotaResolver.Resolve(applicable, extra).Limit;
            IEnumerable<ScopeLimit> personal = command.MonthlyTokens is { } t ? [new ScopeLimit(QuotaScopes.User, id, t)] : [];
            var proposed = QuotaResolver.Resolve(applicable.Where(l => l.Kind != QuotaScopes.User).Concat(personal), extra).Limit;
            // Approved extras count: the person's effective limit may not end above the cap, unless it only goes down.
            if (cap is { } c && proposed > c && !(proposed <= current))
                return Outcome<LimitResult>.Forbidden(string.Create(CultureInfo.InvariantCulture,
                    $"That would take {org.PersonName(id)} above their team or department quota of {c:N0} tokens (approved extras included). Only the owner can go higher."));
        }

        var previous = await store.SetLimitAsync(kind, id, command.MonthlyTokens, viewer.UserId, clock.GetUtcNow(), ct);
        await mediator.Send(new RecordAuditCommand("quota.limit.set", viewer.UserId, $"{kind}:{id}", new
        {
            kind, id, name = org.ScopeName(kind, id), monthlyTokens = command.MonthlyTokens, previous
        }), CancellationToken.None);
        return Outcome<LimitResult>.Ok(new LimitResult(kind, id, command.MonthlyTokens));
    }
}
