namespace Skanyxx.Module.Tickets.Engine;

/// <summary>The stage that sent this one back, what it said, which pass this is, and a person's note if one rejected it.</summary>
public sealed record Rejection(PriorStage By, int Attempt, string? Note);
