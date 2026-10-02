namespace Skanyxx.Module.Identity.Endpoints;

/// <summary><see cref="ClientSecret"/> omitted, null or empty keeps the stored secret.</summary>
public sealed class EntraSettingsRequest
{
    public bool Enabled { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public List<EntraGroupRequest>? Groups { get; set; }
}
