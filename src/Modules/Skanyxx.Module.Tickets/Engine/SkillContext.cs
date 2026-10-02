using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary><see cref="Fence"/> wraps text that came from a ticket or an agent, so the model reads it as data.</summary>
public sealed record SkillContext(Ticket Ticket, PipelineStage Stage, IReadOnlyList<PriorStage> Prior, Func<string, string> Fence);
