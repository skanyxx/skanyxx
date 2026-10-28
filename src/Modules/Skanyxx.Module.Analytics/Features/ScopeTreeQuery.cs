using MediatR;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Features;

internal sealed record ScopeTreeQuery(AnalyticsViewer Viewer) : IRequest<IReadOnlyList<ScopeNode>>;
