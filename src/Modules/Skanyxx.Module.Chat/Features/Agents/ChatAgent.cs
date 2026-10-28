namespace Skanyxx.Module.Chat.Features.Agents;

/// <param name="Model">The model kagent reports for it (or its ModelConfig), when known.</param>
/// <param name="AcceptsImages">Labelled to take images (<see cref="MergedAgents.ImagesLabel"/>).</param>
internal sealed record ChatAgent(string Name, string Namespace, string Description, bool Ready, string? Model, bool AcceptsImages);
