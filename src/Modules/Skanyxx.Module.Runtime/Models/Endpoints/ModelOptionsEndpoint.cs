using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Runtime.Models.Features;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

/// <summary>The form's providers and model suggestions: the owner's, who alone fills the form.</summary>
internal sealed class ModelOptionsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("options");
        Group<ModelsGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ModelOptionsQuery(), ct)).ToHttp());
}
