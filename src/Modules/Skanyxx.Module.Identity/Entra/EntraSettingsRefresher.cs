using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// Loads the settings before Kestrel listens (<see cref="StartingAsync"/>, after <see cref="IdentityMigrator"/>, which is
/// registered first), then checks the version every <see cref="Interval"/>, so a save on another replica applies here
/// within that time. A save on this replica applies at once.
/// </summary>
internal sealed class EntraSettingsRefresher(IServiceScopeFactory scopes, EntraSettingsCache cache, ILogger<EntraSettingsRefresher> logger)
    : BackgroundService, IHostedLifecycleService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public Task StartingAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The current settings stay in force; the next tick retries.
                logger.LogWarning(ex, "Checking the Microsoft sign-in settings for changes failed");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await cache.RefreshAsync(scope.ServiceProvider.GetRequiredService<AccountsDbContext>(), ct);
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
