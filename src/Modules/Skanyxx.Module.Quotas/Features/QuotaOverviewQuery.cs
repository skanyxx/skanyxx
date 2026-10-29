using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Contracts;

namespace Skanyxx.Module.Quotas.Features;

public sealed record QuotaOverviewQuery(QuotaViewer Viewer) : IRequest<Outcome<QuotaOverviewDto>>;
