using System.Text.Json;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Features;
using Skanyxx.Module.Sandboxes.Features.Tasks;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

/// <summary>
/// Server-sent events: one <c>event: &lt;kind&gt;</c> frame per watch update, ending with <c>final</c>, <c>gone</c> or
/// <c>error</c>, and a <c>: keepalive</c> comment while AX is quiet. Open watches are capped per user and in total (429).
/// The limiter is resolved per request, not injected: endpoints are built at startup, also while the module is off.
/// </summary>
internal sealed class WatchTaskEndpoint(IMediator mediator) : Endpoint<TaskRouteRequest>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override void Configure()
    {
        Get("tasks/{name}/watch");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(TaskRouteRequest req, CancellationToken ct)
    {
        // The slot is taken before the existence check, so a caller at its cap costs AX nothing. No user: validation 400s.
        var user = SandboxesHeaders.User(HttpContext);
        var watches = Resolve<WatchLimiter>();
        if (user is not null && !watches.TryEnter(user))
        {
            await Send.ResultAsync(Results.Problem("Too many open watches; close one, or poll GET instead.",
                statusCode: StatusCodes.Status429TooManyRequests));
            return;
        }

        try
        {
            var outcome = await mediator.Send(new WatchTaskQuery(user, req.Name), ct);
            if (outcome.Status != OutcomeStatus.Ok)
            {
                await Send.ResultAsync(outcome.ToHttp(_ => ""));
                return;
            }

            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            HttpContext.Response.ContentType = "text/event-stream";
            HttpContext.Response.Headers.CacheControl = "no-cache";
            HttpContext.Response.Headers["X-Accel-Buffering"] = "no";
            await foreach (var frame in outcome.Value!)
            {
                await HttpContext.Response.WriteAsync(frame.Kind == WatchEventKinds.KeepAlive
                    ? ": keepalive\n\n"
                    : $"event: {frame.Kind}\ndata: {JsonSerializer.Serialize(frame, Json)}\n\n", ct);
                await HttpContext.Response.Body.FlushAsync(ct);
            }
        }
        finally
        {
            if (user is not null)
                watches.Exit(user);
        }
    }
}
