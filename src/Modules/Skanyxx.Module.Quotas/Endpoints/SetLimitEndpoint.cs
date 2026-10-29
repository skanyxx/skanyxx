using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary><c>PUT api/quotas/limits/{kind}/{id}</c>: the owner any; a supervisor their members' personal limits (the handler checks).</summary>
internal sealed class SetLimitEndpoint(IMediator mediator) : Endpoint<SetLimitRequest>
{
    public override void Configure()
    {
        Put("limits/{kind}/{id}");
        Group<QuotasGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(SetLimitRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new SetLimitCommand(QuotaViewer.From(User), req.Kind ?? "", req.Id ?? "", req.MonthlyTokens), ct)).ToHttp());
}
