using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Skanyxx.Core;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages;

/// <summary>The owner's diagnostics: this Host's version and mode, its modules, and every health check (kagent included).</summary>
[Authorize(Roles = SkanyxxRoles.Owner)]
public sealed class DebugModel(HealthCheckService health, IReadOnlyList<IModule> modules, IConfiguration configuration) : PageModel
{
    public string Version { get; } =
        typeof(DebugModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public HostMode Mode { get; } = HostModes.Read(configuration);

    public IReadOnlyList<IModule> Modules => modules;

    /// <summary>Modules switched off with <c>Modules:Enabled:&lt;id&gt;=false</c>.</summary>
    public IReadOnlyList<string> Disabled { get; } = configuration.GetSection("Modules:Enabled").GetChildren()
        .Where(c => bool.TryParse(c.Value, out var on) && !on).Select(c => c.Key).Order().ToList();

    public HealthReport Health { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Health = await health.CheckHealthAsync(ct);
}
