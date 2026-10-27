using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>
/// Who may read the session's index entry (D237): <c>personal:{owner}</c> takes it back. Its owner only, into a scope
/// they may write (see <see cref="Access.SessionAccess.CanShareToAsync"/>). The transcript stays in kagent, the owner's.
/// </summary>
internal sealed record ShareSessionCommand(ChatReader User, string SessionId, string Scope) : IRequest<Outcome<SessionSummary>>;
