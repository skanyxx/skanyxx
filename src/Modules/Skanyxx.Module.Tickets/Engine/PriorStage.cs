using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>An earlier stage's latest output, as a later stage is shown it.</summary>
public sealed record PriorStage(string StageId, StageKind Kind, string Title, string Output);
