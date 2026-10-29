using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>
/// The roles that use the studio (<see cref="Skanyxx.Core.Platform.Studio.StudioUser.CanEnter"/>), set on each endpoint
/// as well as checked in its handler (D375): an employee is refused before any handler runs.
/// </summary>
internal static class StudioRoles
{
    public static readonly string[] People = [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder];
}
