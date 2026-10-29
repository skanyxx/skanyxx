using Microsoft.Extensions.Configuration;

namespace Skanyxx.Core.Platform.Settings;

/// <summary>
/// Cloud tools is a paid feature (D354). The licence check is deliberately simple for now: the deployment sets
/// <c>Skanyxx:Licence:CloudTools=true</c> (Helm <c>skanyxx.extraEnv</c>), which the owner cannot change from the app.
/// A signed licence file replaces this flag when licensing is built; callers only ask <see cref="IsLicensed"/>.
/// </summary>
public static class CloudToolsFeature
{
    public const string LicenceKey = "Skanyxx:Licence:CloudTools";

    public static bool IsLicensed(IConfiguration configuration) => bool.TryParse(configuration[LicenceKey], out var on) && on;

    public static bool IsOn(IConfiguration configuration) =>
        IsLicensed(configuration) && bool.TryParse(configuration["Features:CloudTools"], out var on) && on;
}
