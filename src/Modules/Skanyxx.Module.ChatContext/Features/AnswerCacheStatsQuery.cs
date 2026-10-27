using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Cache;

namespace Skanyxx.Module.ChatContext.Features;

/// <summary>The person's own cache savings; the owner also sees the install's (the Dashboard's token-savings figure).</summary>
internal sealed record AnswerCacheStatsQuery(ChatReader User) : IRequest<Outcome<AnswerCacheStats>>;
