namespace Skanyxx.Module.Identity.Data;

/// <summary>One saved installation setting (D350): a catalog key and its value as text. No row: deployment configuration or the default.</summary>
public sealed class InstallationSettingRow
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedUtc { get; set; }
}
