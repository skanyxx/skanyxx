using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Contracts;

/// <param name="KagentReason">kagent's own words for a failed step; null for anyone but builders and up (D556).</param>
public sealed record StepRunDto(string StepId, StepState State, DateTime? StartedAt, DateTime? EndedAt, string? Output, string? Error, string? KagentReason);
