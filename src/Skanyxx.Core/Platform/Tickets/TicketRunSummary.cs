namespace Skanyxx.Core.Platform.Tickets;

public sealed record TicketRunSummary(
    Guid Id, string TicketKey, string PipelineName, string State, string CreatedBy, DateTime CreatedAt, DateTime UpdatedAt);
