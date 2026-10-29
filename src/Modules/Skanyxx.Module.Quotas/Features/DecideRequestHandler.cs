using System.Globalization;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Contracts;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// The owner decides any request; a supervisor those of their own teams' members (never their own), and approves only
/// while the person stays within their cap (D333, D339). Deciding is one conditional update, so a
/// request is decided once. Audited with the amount, never the reason.
/// </summary>
internal sealed class DecideRequestHandler(OrgReader orgs, QuotaLedger ledger, QuotaStore store, IMediator mediator, TimeProvider clock)
    : IRequestHandler<DecideRequestCommand, Outcome<QuotaRequestDto>>
{
    private const string NotYours = "You can decide requests only from the members of your own teams.";

    public async Task<Outcome<QuotaRequestDto>> Handle(DecideRequestCommand command, CancellationToken ct)
    {
        var viewer = command.Viewer;
        var org = await orgs.ReadAsync(ct);
        var request = await store.RequestAsync(command.Id, ct);
        if (request is null)
            return viewer.IsOwner ? Outcome<QuotaRequestDto>.NotFound("No such request.") : Outcome<QuotaRequestDto>.Forbidden(NotYours);
        if (!viewer.MayActFor(request.UserId, org))
            return Outcome<QuotaRequestDto>.Forbidden(NotYours);
        if (request.Status != RequestStatuses.Pending)
            return Outcome<QuotaRequestDto>.Conflict(QuotaRequestDto.From(request, org), "This request was already decided.", "decided");

        var now = clock.GetUtcNow();
        var month = QuotaMonth.Of(now);
        if (command.Approve && !viewer.IsOwner)
        {
            var (teams, departments) = (org.TeamsOf(request.UserId).Select(t => t.Slug).ToList(), org.DepartmentsOf(request.UserId));
            var book = await ledger.BookAsync(month, [request.UserId], ct);
            var cap = QuotaResolver.SupervisorCap(book.ApplicableTo(request.UserId, teams, departments));
            var limit = book.For(request.UserId, teams, departments).Quota.Limit;
            if (cap is { } c && limit is { } l && l + request.Amount > c)
                return Outcome<QuotaRequestDto>.Forbidden(string.Create(CultureInfo.InvariantCulture,
                    $"Approving this would give {org.PersonName(request.UserId)} more than {c:N0} tokens a month: their team or department limit, or with none, their personal limit. Only the owner can approve it."));
        }

        var status = command.Approve ? RequestStatuses.Approved : RequestStatuses.Denied;
        if (!await store.DecideAsync(request.Id, status, viewer.UserId, now, month, ct))
            return Outcome<QuotaRequestDto>.Conflict(QuotaRequestDto.From((await store.RequestAsync(request.Id, CancellationToken.None))!, org),
                "This request was already decided.", "decided");
        await mediator.Send(new RecordAuditCommand($"quota.request.{status}", viewer.UserId, request.UserId, new
        {
            requestId = request.Id, person = org.PersonName(request.UserId), amount = request.Amount
        }), CancellationToken.None);
        return Outcome<QuotaRequestDto>.Ok(QuotaRequestDto.From(
            (await store.RequestAsync(request.Id, CancellationToken.None))!, org));
    }
}
