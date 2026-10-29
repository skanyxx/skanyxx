using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>
/// The caller's own file, always as a download: <c>application/octet-stream</c>, <c>attachment</c>, <c>nosniff</c> and a
/// sandboxing CSP, so no uploaded content ever renders or runs in Skanyxx's origin. Anyone else's file is 404.
/// </summary>
internal sealed class ChatFileEndpoint(IMediator mediator, ChatFiles files) : Endpoint<FileRouteRequest>
{
    public override void Configure()
    {
        Get("api/chat/files/{id}");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(FileRouteRequest req, CancellationToken ct)
    {
        var owner = Caller.UserId(User)!;
        var outcome = await mediator.Send(new ChatFileQuery(owner, req.Id), ct);
        if (outcome.Value is not { } file)
        {
            await Send.ResultAsync(outcome.ToHttp(_ => ""));
            return;
        }
        // Opened before any header is set: bytes deleted since the lookup are a 404, not a response cut after its headers.
        await using var content = files.OpenRead(owner, file);
        if (content is null)
        {
            await Send.ResultAsync(Outcome<StoredChatFile>.NotFound("No such file.").ToHttp(_ => ""));
            return;
        }
        var response = HttpContext.Response;
        response.ContentType = "application/octet-stream";
        response.ContentLength = content.Length;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = file.Name }.ToString();
        await content.CopyToAsync(response.Body, ct);
    }
}
