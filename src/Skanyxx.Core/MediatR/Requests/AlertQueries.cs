using MediatR;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.MediatR.Requests;

public record GetAllAlertsQuery(string? Severity = null, string? Status = null) : IRequest<List<Alert>>;
public record GetAlertSummaryQuery : IRequest<AlertSummary>;
