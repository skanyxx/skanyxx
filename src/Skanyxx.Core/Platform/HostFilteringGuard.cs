using Microsoft.Extensions.Hosting;

namespace Skanyxx.Core.Platform;

/// <summary>Host filtering is the first defence against DNS rebinding, so a wildcard is only tolerated in Development.</summary>
public static class HostFilteringGuard
{
    public static void EnsureAllowedHosts(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return;

        var hosts = (configuration["AllowedHosts"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Contains("*"))
            throw new InvalidOperationException(
                $"AllowedHosts must list the host names this instance is served under (e.g. \"localhost;127.0.0.1;[::1]\"); " +
                $"\"*\" or empty is only allowed in Development. Environment: {environment.EnvironmentName}.");
    }
}
