using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Contracts;

public sealed record StepRunDto(string StepId, StepState State, DateTime? StartedAt, DateTime? EndedAt, string? Output, string? Error);
