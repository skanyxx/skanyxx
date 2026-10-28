using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>The query's hits for download (<paramref name="Format"/> <c>csv</c> or <c>json</c>); the export is itself an audit row.</summary>
internal sealed record ExportAuditCommand(AuditFilter Filter, bool Ascending, string Format, string ActorId) : IRequest<Outcome<AuditExport>>
{
    public const int MaxRows = 10_000;

    public static readonly IReadOnlyList<string> Formats = ["csv", "json"];
}
