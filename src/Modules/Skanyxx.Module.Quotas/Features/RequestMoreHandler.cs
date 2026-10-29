using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Contracts;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>Anyone with a limit may ask for more, one open request at a time. The audit row has the amount, never the reason.</summary>
internal sealed class RequestMoreHandler(IOrgMembership membership, OrgReader orgs, QuotaLedger ledger, QuotaStore store, IMediator mediator, TimeProvider clock)
    : IRequestHandler<RequestMoreCommand, Outcome<QuotaRequestDto>>
{
    public async Task<Outcome<QuotaRequestDto>> Handle(RequestMoreCommand command, CancellationToken ct)
    {
        var me = command.Viewer.UserId;
        var now = clock.GetUtcNow();
        var member = await membership.ForUserAsync(me, ct);
        var quota = (await ledger.BookAsync(QuotaMonth.Of(now), [me], ct)).For(me, member.Teams, member.Departments);
        if (quota.Quota.Limit is null)
            return Outcome<QuotaRequestDto>.Conflict(null, "You have no token limit, so there is nothing to ask for.", "unlimited");
        if (await store.AddRequestAsync(me, command.Amount, command.Reason.Trim(), now, ct) is not { } request)
            return Outcome<QuotaRequestDto>.Conflict(null, "You already have a request waiting for an answer.", "pending");
        await mediator.Send(new RecordAuditCommand("quota.requested", me, me, new { requestId = request.Id, amount = request.Amount }),
            CancellationToken.None);
        return Outcome<QuotaRequestDto>.Created(QuotaRequestDto.From(request, await orgs.ReadAsync(ct)));
    }
}
