using System.Security.Claims;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Setup;

/// <summary>
/// Guides carry infrastructure detail (ports, namespaces, Helm keys, port-forwards), so only the people who run the
/// install read them: the owner and supervisors. Everyone else gets "ask your admin".
/// </summary>
public static class SetupAccess
{
    public const string Roles = $"{SkanyxxRoles.Owner},{SkanyxxRoles.Supervisor}";

    public static bool CanRead(ClaimsPrincipal user) => user.IsInRole(SkanyxxRoles.Owner) || user.IsInRole(SkanyxxRoles.Supervisor);
}
