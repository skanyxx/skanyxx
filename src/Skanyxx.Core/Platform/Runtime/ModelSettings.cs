namespace Skanyxx.Core.Platform.Runtime;

/// <summary>
/// The model every seed agent uses: kagent's ModelConfig <see cref="Ref"/> as Skanyxx shows it to the owner. Never the
/// API key itself — only whether the ModelConfig points at one (<see cref="HasApiKey"/>). <see cref="Provider"/> is null
/// while the ModelConfig does not exist. <see cref="Configured"/>: it exists, kagent accepted it, and it has the
/// credentials its provider needs (Ollama needs none) — the model step of the first hour is done (D3, D5).
/// </summary>
public sealed record ModelSettings(
    string Ref, string? Provider, string? Model, string? BaseUrl, bool HasApiKey, bool Accepted, string? StatusMessage)
{
    public bool Configured => Provider is not null && Accepted && (HasApiKey || Provider == ModelProviders.Ollama);
}
