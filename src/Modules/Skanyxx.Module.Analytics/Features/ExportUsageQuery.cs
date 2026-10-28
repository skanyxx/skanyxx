using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Features;

/// <summary>The owner's CSV of <paramref name="Scope"/> over <paramref name="Range"/>; every export is audited.</summary>
internal sealed record ExportUsageQuery(AnalyticsViewer Viewer, AnalyticsScope Scope, DateRange Range) : IRequest<Outcome<string>>;
