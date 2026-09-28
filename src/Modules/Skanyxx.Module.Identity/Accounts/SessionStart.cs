using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// When the password sign-in happened, carried in the cookie's authentication properties. They survive sliding
/// renewal and security-stamp refreshes (which reset <c>IssuedUtc</c>), so the absolute session cap is measured from it.
/// </summary>
internal static class SessionStart
{
    private const string Key = "skanyxx.auth_time";

    public static AuthenticationProperties Stamp(DateTimeOffset now) =>
        new() { Items = { [Key] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) } };

    public static DateTimeOffset? Read(AuthenticationProperties properties) =>
        properties.Items.TryGetValue(Key, out var value) && long.TryParse(value, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
