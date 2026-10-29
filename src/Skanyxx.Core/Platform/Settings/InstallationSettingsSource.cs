using Microsoft.Extensions.Configuration;

namespace Skanyxx.Core.Platform.Settings;

/// <summary>Hands the configuration the one <see cref="InstallationSettingsProvider"/> the services also hold.</summary>
public sealed class InstallationSettingsSource(InstallationSettingsProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}
