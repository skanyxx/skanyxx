namespace Skanyxx.Host.Pages;

/// <summary>One row of the group map form on the Entra page; a row whose group id is left empty is dropped.</summary>
public sealed class EntraGroupRow
{
    public string? GroupId { get; set; }
    public string? Label { get; set; }
    public string[] Roles { get; set; } = [];
    public string[] Teams { get; set; } = [];
}
