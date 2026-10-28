using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed record AuditAnalyticsQuery(AuditFilter Filter) : IRequest<Outcome<AuditAnalytics>>;
