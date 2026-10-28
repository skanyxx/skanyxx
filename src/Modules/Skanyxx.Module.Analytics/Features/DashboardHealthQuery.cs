using MediatR;
using Skanyxx.Module.Analytics.Health;

namespace Skanyxx.Module.Analytics.Features;

internal sealed record DashboardHealthQuery : IRequest<IReadOnlyList<HealthTile>>;
