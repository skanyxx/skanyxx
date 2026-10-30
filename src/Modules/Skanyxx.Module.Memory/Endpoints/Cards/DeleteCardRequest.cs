namespace Skanyxx.Module.Memory.Endpoints.Cards;

/// <summary><c>?version=</c>: the version the person saw (D041).</summary>
public sealed class DeleteCardRequest : CardRouteRequest
{
    public int Version { get; set; }
}
