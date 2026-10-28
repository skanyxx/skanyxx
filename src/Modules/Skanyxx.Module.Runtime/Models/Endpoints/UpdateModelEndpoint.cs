using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

/// <summary><c>409 key</c> when the stored key would go to a new endpoint or a keyed provider would be left without one.</summary>
internal sealed class UpdateModelEndpoint(IMediator mediator) : Endpoint<ModelForm>
{
    public override void Configure()
    {
        Put("{namespace}/{name}");
        Group<ModelsGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    // The route names the model, whatever the body says.
    public override async Task HandleAsync(ModelForm req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            ModelCommands.Save(User, Route<string>("namespace")!, Route<string>("name")!, create: false, req), ct)).ToHttp());
}
