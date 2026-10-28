using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>Hits over time, stacked by outcome, with the bucket size picked by <see cref="AuditInterval"/>.</summary>
internal sealed record AuditHistogramQuery(AuditFilter Filter) : IRequest<Outcome<AuditHistogram>>;
