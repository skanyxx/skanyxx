using MediatR;
using Skanyxx.Module.Analytics.Health;

namespace Skanyxx.Module.Analytics.Features;

internal sealed class DashboardHealthHandler(HealthProbe probe) : IRequestHandler<DashboardHealthQuery, IReadOnlyList<HealthTile>>
{
    public async Task<IReadOnlyList<HealthTile>> Handle(DashboardHealthQuery query, CancellationToken ct) =>
        HealthBoard.Tiles(await probe.ReadAsync(ct));
}
