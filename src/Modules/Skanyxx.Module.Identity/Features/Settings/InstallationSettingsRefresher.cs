using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Settings;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Settings;

/// <summary>
/// Copies the saved settings into the configuration (D350): at start, after every save on this replica, and every
/// <see cref="Interval"/> for saves on other replicas. The first load is in <see cref="StartAsync"/>, after the identity
/// migrator and before Kestrel listens, so no request is served on the deployment's values when the owner saved others
/// (Microsoft sign-in switched off, say). Without the Host's <see cref="InstallationSettingsProvider"/> (module tests) it
/// does nothing.
/// </summary>
internal sealed class InstallationSettingsRefresher(
    IServiceScopeFactory scopes, IServiceProvider services, ILogger<InstallationSettingsRefresher> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly InstallationSettingsProvider? _provider = services.GetService<InstallationSettingsProvider>();

    // One read-and-apply at a time: a poll that read before a save committed must not apply after the save's reload.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ReloadAsync(CancellationToken ct)
    {
        if (_provider is null)
            return;
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var saved = await db.InstallationSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
            _provider.Apply(saved);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await TryReloadAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await TryReloadAsync(stoppingToken);
    }

    private async Task TryReloadAsync(CancellationToken ct)
    {
        try
        {
            await ReloadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Installation settings could not be read; the last values stay in use");
        }
    }
}
