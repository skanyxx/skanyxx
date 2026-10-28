using MediatR;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Features;

internal sealed record DashboardSummaryQuery(AnalyticsViewer Viewer) : IRequest<DashboardSummary>;
