using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Features.Runs;
using Skanyxx.Module.Investigate.Reports;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Endpoints;

internal sealed class PdfReportEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Get("runs/{id}/report.pdf");
        Group<InvestigateGroup>();
        Roles(InvestigateGroup.Roles);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetRunQuery(Viewer.From(User), req.Id), ct);
        if (outcome.Status != OutcomeStatus.Ok)
        {
            await Send.ResultAsync(outcome.ToHttp(r => r.Id));
            return;
        }
        await Send.BytesAsync(PdfReport.Render(RunReport.From(outcome.Value!)), $"investigation-{req.Id:N}.pdf", "application/pdf", cancellation: ct);
    }
}
