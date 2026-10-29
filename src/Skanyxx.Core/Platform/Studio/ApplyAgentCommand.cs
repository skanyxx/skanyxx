using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// "Direct by role, PR for users" (D366): a supervisor or the owner applies a form at once. It still goes through git —
/// one pull request, merged by them through the same gate — so main stays the only truth and the change has a history.
/// Builders and employees propose instead (403 here).
/// </summary>
public sealed record ApplyAgentCommand(StudioUser User, AgentDraft Draft) : IRequest<Outcome<StudioResult>>;
