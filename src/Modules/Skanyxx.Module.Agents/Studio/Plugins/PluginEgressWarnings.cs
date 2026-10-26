using Microsoft.Extensions.Hosting;
using Skanyxx.Module.Agents.Plugins;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// The plugin switches that widen where kagent's controller sends header values (sec3 R4): Skanyxx cannot fence that
/// connection itself, since kagent resolves and connects, so outside Development each one is said once at startup.
/// </summary>
internal static class PluginEgressWarnings
{
    private const string Fence = "Turn it off, or fence the controller's egress (Helm networkPolicy.controllerEgress).";

    public static IEnumerable<string> Of(IHostEnvironment environment, PluginsOptions options)
    {
        if (environment.IsDevelopment())
            yield break;
        if (options.AllowHttp)
            yield return "Plugins:AllowHttp is on outside Development: kagent's controller may send plugin header values over plain HTTP, "
                + "to any service in the cluster a plugin server's URL names. " + Fence;
        if (options.AllowPrivateServers)
            yield return "Plugins:AllowPrivateServers is on outside Development: a plugin server may name an internal address, and kagent's "
                + "controller connects to it from inside the cluster with the owner's header values. " + Fence;
    }
}
