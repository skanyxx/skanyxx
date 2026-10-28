using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>A page of the hits, newest first unless <paramref name="Ascending"/>, keyset-paged after <paramref name="After"/>.</summary>
internal sealed record SearchAuditQuery(AuditFilter Filter, bool Ascending = false, int Limit = 50, AuditCursor? After = null)
    : IRequest<Outcome<AuditPage>>;
