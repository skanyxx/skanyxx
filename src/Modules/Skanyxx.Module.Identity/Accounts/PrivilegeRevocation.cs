using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Publishes <see cref="PrivilegesRevoked"/> once the change is committed. Not cancellable: the change is saved, so
/// its follow-up must not depend on the owner's browser staying connected. A failing handler is logged at Error and
/// rethrown (the caller gets a 500); saving the same change again publishes again.
/// </summary>
internal sealed class PrivilegeRevocation(IPublisher publisher, ILogger<PrivilegeRevocation> logger)
{
    public async Task PublishAsync(string userId, string reason, string actorId)
    {
        try
        {
            await publisher.Publish(new PrivilegesRevoked(userId, reason), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Revoking what {UserId} issued ({Reason}) failed after {ActorUserId}'s change was saved; save the same change again to retry",
                userId, reason, actorId);
            throw;
        }
    }
}
