using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Features;

/// <summary><paramref name="Viewer"/>'s report on <paramref name="Scope"/>; Forbidden unless <see cref="ScopeResolver"/> allows it.</summary>
internal sealed record UsageReportQuery(AnalyticsViewer Viewer, AnalyticsScope Scope, DateRange Range, bool Compare)
    : IRequest<Outcome<UsageReport>>;
