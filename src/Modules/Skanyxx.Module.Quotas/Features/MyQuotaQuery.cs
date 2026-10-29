using MediatR;
using Skanyxx.Module.Quotas.Contracts;

namespace Skanyxx.Module.Quotas.Features;

public sealed record MyQuotaQuery(QuotaViewer Viewer) : IRequest<MyQuotaDto>;
