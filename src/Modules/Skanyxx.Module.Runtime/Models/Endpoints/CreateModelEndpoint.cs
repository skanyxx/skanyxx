using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

/// <summary><c>201</c> created in kagent; <c>400</c> the form's problems; <c>409</c> the name is taken.</summary>
internal sealed class CreateModelEndpoint(IMediator mediator) : Endpoint<ModelForm>
{
    public override void Configure()
    {
        Post("/" + ModelsGroup.Prefix);
        ModelsGroup.Apply(Definition);
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(ModelForm req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(ModelCommands.Save(User, req.Namespace ?? "", req.Name ?? "", create: true, req), ct)).ToHttp());
}
