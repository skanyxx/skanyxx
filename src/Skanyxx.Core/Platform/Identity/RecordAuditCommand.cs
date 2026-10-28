using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// One row in the identity audit (D152) from another module, so every audited action is on the owner's Audit page.
/// <paramref name="Action"/> is <c>&lt;area&gt;.&lt;verb&gt;</c>; <paramref name="Details"/> is serialized to JSON and must
/// never hold a token, password, secret or prompt text.
/// </summary>
public sealed record RecordAuditCommand(string Action, string? ActorId, string? TargetId, object? Details) : IRequest;
