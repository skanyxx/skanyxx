using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Issues (or rotates) a studio agent's memory secret: created by <c>studio</c>, never acting for users, only for a name the
/// studio owns (D117) and not suspended (D121). The plaintext is in the result only, for the reconciler to hand to kagent.
/// </summary>
public sealed record IssueStudioSecretCommand(string Actor, string AgentId) : IRequest<Outcome<StudioSecret>>;
