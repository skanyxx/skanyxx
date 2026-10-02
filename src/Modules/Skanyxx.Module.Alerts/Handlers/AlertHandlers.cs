using MediatR;
using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.Alerts.Handlers;

public class GetAllAlertsHandler : IRequestHandler<GetAllAlertsQuery, List<Alert>>
{
    private readonly IAlertService _svc;
    public GetAllAlertsHandler(IAlertService svc) => _svc = svc;
    public Task<List<Alert>> Handle(GetAllAlertsQuery req, CancellationToken ct) => _svc.GetAllAsync(req.Severity, req.Status);
}

public class GetAlertSummaryHandler : IRequestHandler<GetAlertSummaryQuery, AlertSummary>
{
    private readonly IAlertService _svc;
    public GetAlertSummaryHandler(IAlertService svc) => _svc = svc;
    public Task<AlertSummary> Handle(GetAlertSummaryQuery req, CancellationToken ct) => _svc.GetStatsAsync();
}
