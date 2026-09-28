using System.Text;
using System.Text.Json;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Features.Runs;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

/// <summary>The run's evidence as JSONL: one line per (stage, attempt, agent), with the exact prompt each agent got.</summary>
internal sealed class GetDatasetEndpoint(IMediator mediator) : Endpoint<RunRouteRequest>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override void Configure()
    {
        Get("runs/{id}/dataset");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(RunRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetRunQuery(Caller.UserId(User), req.Id, WithPrompts: true), ct);
        await Send.ResultAsync(outcome.Status == OutcomeStatus.Ok
            ? Results.Text(string.Concat(outcome.Value!.ToDataset().Select(row => JsonSerializer.Serialize(row, Json) + "\n")),
                "application/x-ndjson", Encoding.UTF8)
            : outcome.ToHttp(r => r.Id));
    }
}
