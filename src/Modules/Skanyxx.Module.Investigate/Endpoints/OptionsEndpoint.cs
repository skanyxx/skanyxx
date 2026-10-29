using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Options;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class OptionsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("options");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetOptionsQuery(Viewer.From(User)), ct)).ToHttp());
}
