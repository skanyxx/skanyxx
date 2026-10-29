using MediatR;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Quotas.Contracts;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

internal sealed class MyQuotaHandler(OrgReader orgs, QuotaLedger ledger, QuotaStore store, TimeProvider clock)
    : IRequestHandler<MyQuotaQuery, MyQuotaDto>
{
    public const int RecentRequests = 10;

    public async Task<MyQuotaDto> Handle(MyQuotaQuery query, CancellationToken ct)
    {
        var me = query.Viewer.UserId;
        var month = QuotaMonth.Of(clock.GetUtcNow());
        var org = await orgs.ReadAsync(ct);
        var book = await ledger.BookAsync(month, [me], ct);
        var teams = org.TeamsOf(me).Select(t => t.Slug).ToList();
        var departments = org.DepartmentsOf(me);
        var requests = await store.RequestsAsync([me], RecentRequests, ct);
        return new MyQuotaDto(month.ToString("yyyy-MM"), QuotaMonth.ResetsOn(month),
            PersonQuotaDto.From(book.For(me, teams, departments), org, book.ApplicableTo(me, teams, departments)),
            [.. requests.Select(r => QuotaRequestDto.From(r, org))], requests.Any(r => r.Status == RequestStatuses.Pending),
            QuotaPrice.Currency, QuotaCostDisclaimer.Title, QuotaCostDisclaimer.Text);
    }
}
