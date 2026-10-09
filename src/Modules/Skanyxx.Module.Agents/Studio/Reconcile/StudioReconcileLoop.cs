using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>
/// The periodic pass (D3): repairs what a merge-triggered apply could not finish (kagent or git down, a crash between
/// merge and apply), creates the repo setup asked for (D023, D119), and removes previews of closed proposals. Off when no
/// git is configured, and idle until setup has made the owner; setup pokes it (<see cref="StudioSignal"/>) instead of
/// creating the repo on its own request. Nothing a pass throws stops the Host (M4): it is logged and the next pass runs.
/// </summary>
internal sealed class StudioReconcileLoop(
    StudioReconciler reconciler, StudioSignal signal, IServiceScopeFactory scopes, IOptions<StudioOptions> options, ILogger<StudioReconcileLoop> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Configured)
            return;
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.ReconcileSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await BootstrappedAsync(stoppingToken))
                {
                    var failures = await reconciler.ReconcileAsync(stoppingToken);
                    if (failures > 0)
                        logger.LogWarning("Studio reconcile finished with {Failures} agent(s) not applied", failures);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or System.Data.Common.DbException)
            {
                logger.LogWarning(ex, "Studio reconcile could not run: {Reason}", ex.Message);
            }
            catch (Exception ex)
            {
                // A person must act (the repo is gone or not the recorded one, main lost its protection), or git or
                // kagent answered something nobody expected: loudly, and keep going.
                logger.LogError(ex, "Studio reconcile failed: {Reason}", ex.Message);
            }

            try
            {
                await signal.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> BootstrappedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new IdentityStatusQuery(), ct)).Value?.Bootstrapped == true;
    }
}
