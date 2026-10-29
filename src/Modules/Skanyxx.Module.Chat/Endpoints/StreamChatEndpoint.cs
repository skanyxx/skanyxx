using System.Text.Json;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Turns;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>
/// One message, answered as server-sent events (<see cref="ChatStreamEvent"/>). A refusal (validation, no such agent or
/// conversation, a quota) is an ordinary problem response before the stream starts. Closing the request stops the turn.
/// </summary>
internal sealed class StreamChatEndpoint(IMediator mediator) : Endpoint<SendChatRequest>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override void Configure()
    {
        Post("api/chat/stream");
        Options(ChatRoutes.Guard(SendChatEndpoint.MaxBodyBytes));
    }

    public override async Task HandleAsync(SendChatRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new StreamChatMessageCommand(req.Command(User)), ct);
        if (outcome.Value is not { } started)
        {
            await Send.ResultAsync(outcome.ToHttp(_ => ""));
            return;
        }
        await using var stream = started;
        var response = HttpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        await foreach (var e in stream.EventsAsync(ct))
        {
            await response.WriteAsync($"event: {e.Type}\ndata: {JsonSerializer.Serialize(e.Data, e.Data.GetType(), Json)}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
}
