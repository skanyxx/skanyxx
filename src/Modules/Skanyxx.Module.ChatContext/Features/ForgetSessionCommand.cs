using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>Drops the session from the index (not from kagent), and the owner's cached answers with it. Its owner only.</summary>
internal sealed record ForgetSessionCommand(ChatReader User, string SessionId) : IRequest<Outcome<bool>>;
