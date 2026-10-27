using MediatR;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Audit;

namespace Skanyxx.Module.Identity.Features.Audit;

internal sealed class RecordAuditHandler(IdentityAudit audit) : IRequestHandler<RecordAuditCommand>
{
    public Task Handle(RecordAuditCommand command, CancellationToken ct) =>
        audit.WriteAsync(command.Action, command.ActorId, command.TargetId, command.Details, ct);
}
