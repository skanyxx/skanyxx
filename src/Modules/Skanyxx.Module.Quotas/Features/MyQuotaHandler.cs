using MediatR;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Contracts;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// The caller's own month, read after every turn: their membership (as the turn's own check reads it), the teams and
/// departments for their names, and the list of people only to name who decided one of their requests (D384).
/// </summary>
internal sealed class MyQuotaHandler(IOrgMembership membership, OrgReader orgs, QuotaLedger ledger, QuotaStore store, TimeProvider clock)
    : IRequestHandler<MyQuotaQuery, MyQuotaDto>
{
    public const int RecentRequests = 10;

    public async Task<MyQuotaDto> Handle(MyQuotaQuery query, CancellationToken ct)
    {
        var me = query.Viewer.UserId;
        var month = QuotaMonth.Of(clock.GetUtcNow());
        var member = await membership.ForUserAsync(me, ct);
        var book = await ledger.BookAsync(month, [me], ct);
        var requests = await store.RequestsAsync([me], RecentRequests, ct);
        var org = requests.Any(r => r.DecidedBy is not null) ? await orgs.ReadAsync(ct) : await orgs.WithoutPeopleAsync(ct);
        return new MyQuotaDto(month.ToString("yyyy-MM"), QuotaMonth.ResetsOn(month),
            PersonQuotaDto.From(book.For(me, member.Teams, member.Departments), org, book.ApplicableTo(me, member.Teams, member.Departments)),
            [.. requests.Select(r => QuotaRequestDto.From(r, org))], requests.Any(r => r.Status == RequestStatuses.Pending),
            book.Cost.Currency, QuotaCostDisclaimer.Title, QuotaCostDisclaimer.Text);
    }
}
