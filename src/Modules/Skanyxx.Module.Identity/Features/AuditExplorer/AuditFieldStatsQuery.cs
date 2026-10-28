using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>The top <see cref="Top"/> values of every field in <see cref="AuditFields.Names"/> among the query's hits.</summary>
internal sealed record AuditFieldStatsQuery(AuditFilter Filter) : IRequest<Outcome<IReadOnlyList<AuditFieldStats>>>
{
    public const int Top = 10;
}
