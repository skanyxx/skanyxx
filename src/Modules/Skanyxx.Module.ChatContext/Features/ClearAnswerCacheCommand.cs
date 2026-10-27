using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>Stops the person's kept answers matching; with <paramref name="All"/>, everyone's (the owner only).</summary>
internal sealed record ClearAnswerCacheCommand(ChatReader User, bool All) : IRequest<Outcome<bool>>;
