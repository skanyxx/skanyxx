namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// The Microsoft Entra ID sign-in settings (D027): one row, <see cref="SingletonId"/>. The client secret is kept only
/// Data-Protection-protected (<see cref="ProtectedClientSecret"/>). <see cref="Version"/> goes up on every save, so each
/// instance can tell that its copy is stale.
/// </summary>
public sealed class EntraSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string? ProtectedClientSecret { get; set; }
    public int Version { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedUtc { get; set; }
}
