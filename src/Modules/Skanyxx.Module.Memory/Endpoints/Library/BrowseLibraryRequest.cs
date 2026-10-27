using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.Library;

public sealed class BrowseLibraryRequest
{
    [QueryParam] public string? Q { get; set; }
    [QueryParam] public string? Scope { get; set; }
    [QueryParam] public string? Ticket { get; set; }
    [QueryParam] public string? Keyword { get; set; }
    [QueryParam] public string? Type { get; set; }
    [QueryParam] public string? Origin { get; set; }
    [QueryParam] public string? Agent { get; set; }
    [QueryParam] public int Offset { get; set; }
}
