using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// Stored plugin header values are protected by the identity module's Data Protection key ring, which sits unencrypted in
/// its database unless <c>Identity:DataProtectionCertificatePath</c> is set (SEC L1). Fine on a developer's machine; anywhere
/// else a read of that database, or a backup of it beside memory's, opens every third-party credential (D220).
/// </summary>
internal static class PluginValueKeyRing
{
    public const string Warning =
        "Plugin header values are protected by a Data Protection key ring stored unencrypted in the identity database "
        + "(Identity:DataProtectionCertificatePath is not set): anyone who reads that database, or its backup, can decrypt them. "
        + "Set a certificate, and keep the identity and memory databases out of one shared backup.";

    public static bool InClear(IHostEnvironment environment, IConfiguration configuration) =>
        !environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuration["Identity:DataProtectionCertificatePath"]);
}
