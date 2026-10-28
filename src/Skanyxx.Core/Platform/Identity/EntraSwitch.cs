using Microsoft.Extensions.Configuration;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// requirements.md O2 (D272): Microsoft sign-in exists only on an install whose operator turned it on. Off (the default),
/// there is no Entra page, nav item, login button or endpoint; the owner's saved settings stay stored but unused.
/// </summary>
public static class EntraSwitch
{
    public const string Key = "Identity:EntraAvailable";

    public const string Off = "Microsoft sign-in is not available on this install.";

    public static bool IsOn(IConfiguration configuration) => bool.TryParse(configuration[Key], out var on) && on;
}
