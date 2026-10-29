namespace Skanyxx.Host.Pages;

/// <summary>What the Settings page's Sandboxes section reports; read-only (deployment configuration, a restart to change).</summary>
public sealed record SandboxStatus(bool ModuleLoaded, bool Enabled, bool AddressSet, int AllowedImages, bool NetworkConfirmed)
{
    public bool Ready => ModuleLoaded && Enabled && AddressSet && AllowedImages > 0 && NetworkConfirmed;
}
