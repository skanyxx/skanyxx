using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Skanyxx.Core;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Core.Services;
using Skanyxx.Host.Diagnostics;

namespace Skanyxx.Host.Pages;

/// <summary>
/// The owner's diagnostics (D353), and nothing else: build and uptime, configuration sources (secrets redacted), module
/// health, kagent / AX / Postgres / Dragonfly connectivity, the warnings and errors since start, and request timings.
/// Read-only: it changes nothing and runs no tool.
/// </summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class DebugModel(
    HealthCheckService health, IReadOnlyList<IModule> modules, IConfiguration configuration, IWebHostEnvironment environment, DebugLog log)
    : PageModel
{
    public string Version { get; } =
        typeof(DebugModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public string Runtime { get; } = RuntimeInformation.FrameworkDescription;

    public string Platform { get; } = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})";

    public string Environment => environment.EnvironmentName;

    public HostMode Mode { get; } = HostModes.Read(configuration);

    public DateTimeOffset StartedUtc { get; } = ProcessStart();

    public TimeSpan Uptime => DateTimeOffset.UtcNow - StartedUtc;

    public IReadOnlyList<IModule> Modules => modules;

    /// <summary>Modules switched off with <c>Modules:Enabled:&lt;id&gt;=false</c>.</summary>
    public IReadOnlyList<string> Disabled { get; } = configuration.GetSection("Modules:Enabled").GetChildren()
        .Where(c => bool.TryParse(c.Value, out var on) && !on).Select(c => c.Key).Order().ToList();

    public HealthReport Health { get; private set; } = null!;

    public IReadOnlyList<Connectivity> Connections { get; private set; } = [];

    public IReadOnlyList<ConfigSource> Sources { get; } = ConfigurationView.Sources((IConfigurationRoot)configuration);

    public IReadOnlyList<ConfigRow> Settings { get; } = ConfigurationView.Rows((IConfigurationRoot)configuration);

    public IReadOnlyList<LogLine> Lines => log.Lines();

    public IReadOnlyList<RequestTiming> Timings => log.Timings(20);

    public int RequestSamples => log.RequestCount;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var dragonfly = DragonflyProbe.CheckAsync("Dragonfly (memory)", configuration["Memory:Dragonfly:Configuration"], ct);
        Health = await health.CheckHealthAsync(ct);
        Connections =
        [
            From(KAgentHealthCheck.Name, "kagent"),
            From(DashboardChecks.Sandboxes, "AX (sandboxes)"),
            .. Health.Entries.Where(e => e.Key.EndsWith(DashboardChecks.PostgresSuffix, StringComparison.Ordinal))
                .OrderBy(e => e.Key).Select(e => From(e.Key, $"Postgres ({e.Key[..^DashboardChecks.PostgresSuffix.Length]})")),
            await dragonfly,
            From(DashboardChecks.AnswerCache, "Dragonfly (answer cache)"),
        ];
    }

    private static DateTimeOffset ProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }

    private Connectivity From(string check, string name) =>
        Health.Entries.TryGetValue(check, out var entry)
            ? new(name, entry.Status.ToString(), SecretText.Redact(entry.Description ?? entry.Exception?.Message ?? ""), (int)entry.Duration.TotalMilliseconds)
            : new(name, "not configured", null, null);
}
