namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>A row of a top-values query; a null value is a row where the field is empty.</summary>
internal sealed class FieldCountRow
{
    public string? Value { get; set; }
    public long Count { get; set; }
}
