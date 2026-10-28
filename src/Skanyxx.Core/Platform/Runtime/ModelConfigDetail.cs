namespace Skanyxx.Core.Platform.Runtime;

/// <summary>
/// A kagent ModelConfig as Skanyxx shows it. Never the API key: only whether the spec points at one
/// (<see cref="HasApiKey"/>). <see cref="Params"/> are the provider fields the Models form edits, as text; <see cref="Kept"/>
/// names what else the spec holds (TLS, headers, nested provider settings), which an edit keeps as it is and the page
/// never shows. <see cref="IsDefault"/>: it is <c>KAgent:ModelConfig</c>, the model the seed agents and Chat use (D3).
/// </summary>
public sealed record ModelConfigDetail(
    string Namespace, string Name, string Provider, string Model, bool HasApiKey, bool Accepted, string? StatusMessage,
    bool IsDefault, IReadOnlyDictionary<string, string> Params, IReadOnlyList<string> Kept)
{
    public string Ref => $"{Namespace}/{Name}";
}
