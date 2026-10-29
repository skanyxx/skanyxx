using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Contracts;

namespace Skanyxx.Module.Quotas.Features;

public sealed record RequestMoreCommand(QuotaViewer Viewer, long Amount, string Reason) : IRequest<Outcome<QuotaRequestDto>>;
