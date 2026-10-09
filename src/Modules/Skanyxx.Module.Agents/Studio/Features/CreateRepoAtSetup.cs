using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// D023 as built: setup creates the agent repo in the bundled git — off the setup request (m13): this only wakes the
/// reconcile loop, whose first pass creates, protects and records the repo (D119) and retries until git answers. Setup
/// never waits on git and never fails because of it.
/// </summary>
internal sealed class CreateRepoAtSetup(StudioSignal signal, IOptions<StudioOptions> options, ILogger<CreateRepoAtSetup> logger)
    : INotificationHandler<OwnerBootstrapped>
{
    public Task Handle(OwnerBootstrapped notification, CancellationToken ct)
    {
        if (options.Value.Configured)
        {
            signal.Poke();
            logger.LogWarning("Setup: the studio reconciler creates the agent repo now");
        }
        return Task.CompletedTask;
    }
}
