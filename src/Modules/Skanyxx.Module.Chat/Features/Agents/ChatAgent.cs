using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <param name="Model">The model kagent reports for it (or its ModelConfig), when known.</param>
/// <param name="AcceptsImages">Labelled to take images (<see cref="MergedAgents.ImagesLabel"/>).</param>
/// <param name="ToolProblem">
/// Null while its tool servers look fine (or kagent did not list them); else <see cref="ToolServerHealth.Summary"/>: by
/// name for builders and up, a fixed text for anyone else (D554).
/// </param>
internal sealed record ChatAgent(string Name, string Namespace, string Description, bool Ready, string? Model, bool AcceptsImages, string? ToolProblem = null);
