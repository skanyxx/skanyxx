namespace Skanyxx.Module.Runtime.Models.Features;

/// <param name="Models">kagent's supported models for the provider: suggestions, any model id may be typed.</param>
/// <param name="TakesKey">False for Ollama.</param>
internal sealed record ProviderOption(string Type, IReadOnlyList<ProviderField> Fields, IReadOnlyList<string> Models, bool TakesKey);
