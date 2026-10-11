namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>Active sandboxes (see <see cref="Domain.SandboxPhases.IsActive"/>) owned by one user, and in the whole atespace.</summary>
internal sealed record ActiveSandboxCount(int Mine, int All);
