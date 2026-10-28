namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>One chip: <paramref name="Field"/> (from <see cref="AuditFields.Names"/>) is, or with <paramref name="Exclude"/> is not, <paramref name="Value"/>; a null value is "(none)".</summary>
internal sealed record AuditFieldFilter(string Field, string? Value, bool Exclude)
{
    /// <summary><c>field:value</c>, split at the first colon (an IPv6 value keeps its own).</summary>
    public static AuditFieldFilter Parse(string chip, bool exclude)
    {
        var colon = chip.IndexOf(':');
        var (field, value) = colon < 0 ? (chip, "") : (chip[..colon], chip[(colon + 1)..]);
        return new(field, value == AuditFields.None ? null : value, exclude);
    }
}
