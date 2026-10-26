using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins.Features;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>Install an uploaded zip (the owner's only): the same file again, with the digest inspect answered.</summary>
internal sealed class InstallPluginZipEndpoint(IMediator mediator) : Endpoint<PluginZipRequest>
{
    public override void Configure()
    {
        Post("/" + PluginsGroup.Prefix + "/install/zip");
        PluginsGroup.Apply(Definition, PluginsGroup.MaxUploadBytes);
        AllowFileUploads();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(PluginZipRequest req, CancellationToken ct)
    {
        if (req.File?.Length > PluginsGroup.MaxZipBytes)
        {
            await Send.ResultAsync(Results.Problem(PluginUploads.TooLarge, statusCode: StatusCodes.Status413PayloadTooLarge));
            return;
        }
        var (request, upload) = await PluginUploads.ReadAsync(req, ct);
        await Send.ResultAsync((await mediator.Send(new InstallPluginSourceCommand(StudioUser.From(User), request, upload), ct)).ToHttp());
    }
}
