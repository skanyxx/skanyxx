namespace Skanyxx.Module.Identity.Data;

/// <summary>The organization's default appearance and layout (one row, <see cref="SingletonId"/>), which people inherit.</summary>
public sealed class OrganizationPreference
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string Json { get; set; } = "{}";
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedUtc { get; set; }
}
