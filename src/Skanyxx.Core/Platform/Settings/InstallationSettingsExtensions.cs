using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Core.Platform.Settings;

public static class InstallationSettingsExtensions
{
    /// <summary>Adds the saved-settings layer on top of every other configuration source, and registers it for the identity module.</summary>
    public static IServiceCollection AddInstallationSettings(this IServiceCollection services, IConfigurationBuilder configuration)
    {
        var provider = new InstallationSettingsProvider();
        configuration.Add(new InstallationSettingsSource(provider));
        return services.AddSingleton(provider);
    }
}
