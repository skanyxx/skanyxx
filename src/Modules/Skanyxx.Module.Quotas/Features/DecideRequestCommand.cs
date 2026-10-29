using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Contracts;

namespace Skanyxx.Module.Quotas.Features;

public sealed record DecideRequestCommand(QuotaViewer Viewer, long Id, bool Approve) : IRequest<Outcome<QuotaRequestDto>>;
