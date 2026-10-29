using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Quotas.Contracts;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// The owner sees everyone, every limit and every request; a supervisor the members of their teams (in full only those
/// they may act for), the limits that apply to those (read-only above the person) and their requests. Anyone else is
/// refused.
/// </summary>
internal sealed class QuotaOverviewHandler(OrgReader orgs, QuotaLedger ledger, QuotaStore store, TimeProvider clock)
    : IRequestHandler<QuotaOverviewQuery, Outcome<QuotaOverviewDto>>
{
    public const int RecentRequests = 100;

    public async Task<Outcome<QuotaOverviewDto>> Handle(QuotaOverviewQuery query, CancellationToken ct)
    {
        var viewer = query.Viewer;
        if (!viewer.IsSupervisor)
            return Outcome<QuotaOverviewDto>.Forbidden("Only the owner and supervisors manage quotas.");
        var month = QuotaMonth.Of(clock.GetUtcNow());
        var org = await orgs.ReadAsync(ct);
        var people = viewer.People(org);
        var book = await ledger.BookAsync(month, viewer.IsOwner ? null : people, ct);

        var actFor = people.Where(p => viewer.MayActFor(p, org)).Order(StringComparer.Ordinal).ToList();
        // A supervisor sees in full only whom they may act for (and themselves); a fellow member, a peer supervisor or the
        // owner in a shared team is a name and a percentage (D384).
        IReadOnlySet<string> full = viewer.IsOwner ? people : actFor.Append(viewer.UserId).ToHashSet();
        var rows = org.People.Where(p => people.Contains(p.Id)).Select(p =>
        {
            var teams = org.TeamsOf(p.Id).Select(t => t.Slug).ToList();
            var departments = org.DepartmentsOf(p.Id);
            var row = PersonQuotaDto.From(book.For(p.Id, teams, departments), org, book.ApplicableTo(p.Id, teams, departments));
            return full.Contains(p.Id) ? row : row.Redacted();
        }).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var teamsSeen = viewer.IsOwner ? org.Teams : org.TeamsOf(viewer.UserId);
        var departmentsSeen = teamsSeen.Select(t => t.Department).ToHashSet();
        var scopes = teamsSeen.Select(t => new ScopeOption(QuotaScopes.Team, t.Slug, t.Name, t.Department))
            .Concat(org.Departments.Where(d => departmentsSeen.Contains(d.Slug) || viewer.IsOwner)
                .Select(d => new ScopeOption(QuotaScopes.Department, d.Slug, d.Name, null)))
            .OrderBy(s => s.Kind).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var visible = scopes.Select(s => (s.Kind, s.Id)).Concat(full.Select(p => (QuotaScopes.User, p))).ToHashSet();
        var limits = book.Limits.Where(l => visible.Contains((l.Kind, l.Id)))
            .Select(l => new LimitDto(l.Kind, l.Id, org.ScopeName(l.Kind, l.Id), l.MonthlyTokens))
            .OrderBy(l => l.Kind).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // A supervisor sees only the requests they may decide: a fellow supervisor's or the owner's reason is not theirs to read.
        var requests = await store.RequestsAsync(viewer.IsOwner ? null : actFor, RecentRequests, ct);
        var models = book.Usage.Select(u => u.Model).Concat(book.Prices.Keys).Distinct().Order(StringComparer.Ordinal).ToList();
        return Outcome<QuotaOverviewDto>.Ok(new QuotaOverviewDto(
            month.ToString("yyyy-MM"), QuotaMonth.ResetsOn(month), viewer.IsOwner, rows, limits, scopes,
            [.. requests.Select(r => QuotaRequestDto.From(r, org))],
            [.. book.Prices.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new PriceDto(p.Key, p.Value))], models,
            actFor, QuotaPrice.Currency, QuotaCostDisclaimer.Title, QuotaCostDisclaimer.Text));
    }
}
