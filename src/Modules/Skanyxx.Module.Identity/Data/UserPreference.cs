namespace Skanyxx.Module.Identity.Data;

/// <summary>A person's own appearance and layout choices (<c>PreferenceSet</c> as JSON); no row = everything inherited.</summary>
public sealed class UserPreference
{
    public string UserId { get; set; } = "";
    public string Json { get; set; } = "{}";
    public DateTimeOffset UpdatedUtc { get; set; }
}
