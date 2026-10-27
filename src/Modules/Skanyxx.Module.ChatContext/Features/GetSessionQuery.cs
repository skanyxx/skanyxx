using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Data;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>One indexed session; a session this person may not read is "not found", exactly like a missing one.</summary>
internal sealed record GetSessionQuery(ChatReader User, string SessionId) : IRequest<Outcome<SessionSummary>>;
