using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.ChatContext.Cache;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class ClearAnswerCacheHandler(AnswerCache cache, ILogger<ClearAnswerCacheHandler> logger)
    : IRequestHandler<ClearAnswerCacheCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(ClearAnswerCacheCommand command, CancellationToken ct)
    {
        if (command.All && !command.User.IsOwner)
            return Outcome<bool>.Forbidden("Only the owner may clear everyone's cached answers.");
        if (!cache.On)
            return Outcome<bool>.Ok(false);

        if (command.All)
            logger.LogInformation("{UserId} clears every cached chat answer", command.User.UserId);
        return await (command.All ? cache.ClearAllAsync() : cache.ClearAsync(command.User.UserId))
            ? Outcome<bool>.Ok(true)
            : Outcome<bool>.Unavailable("The answer cache did not answer; nothing was cleared.");
    }
}
