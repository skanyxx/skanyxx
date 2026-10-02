using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

public sealed class SearchCardsRequest
{
    [QueryParam]
    public string Q { get; set; } = "";
}
