namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>Active tasks (see <see cref="Domain.TaskPhases.IsActive"/>) owned by one user, and in the whole atespace.</summary>
internal sealed record ActiveTaskCount(int Mine, int All);
