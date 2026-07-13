using MediatR;
using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.Analytics.Handlers;

public class GetSessionAnalyticsHandler : IRequestHandler<GetSessionAnalyticsQuery, SessionAnalytics>
{
    private readonly IAnalyticsService _svc;
    public GetSessionAnalyticsHandler(IAnalyticsService svc) => _svc = svc;
    public Task<SessionAnalytics> Handle(GetSessionAnalyticsQuery req, CancellationToken ct) => _svc.GetSessionAnalyticsAsync(req.Period);
}
