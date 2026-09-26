using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Contracts;

public sealed record RunSummaryDto(
    Guid Id, string TicketKey, string PipelineId, string PipelineName, RunState State, int Cursor, string? Error, string CreatedBy,
    DateTime CreatedAt, DateTime UpdatedAt);
