using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Settings.Model;

/// <summary>Saves the model step (owner only); the API key, if any, goes to kagent and never comes back.</summary>
internal sealed class SaveModelEndpoint(IMediator mediator) : Endpoint<SaveModelRequest>
{
    public override void Configure()
    {
        Put(ModelRoutes.Path);
        Roles(SkanyxxRoles.Owner);
        Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(ModelRoutes.MaxBodyBytes)));
    }

    public override async Task HandleAsync(SaveModelRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SaveModelSettingsCommand(
            Caller.UserId(User)!, req.Provider?.Trim() ?? "", req.Model?.Trim() ?? "", req.ApiKey?.Trim(), req.BaseUrl?.Trim()), ct);
        await Send.ResultAsync(outcome.ToHttp(s => s));
    }
}
