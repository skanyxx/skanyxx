using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Alerts.Services;

public class KAgentAlertService : IAlertService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentAlertService> _logger;

    public KAgentAlertService(KAgentApiClient kagent, ILogger<KAgentAlertService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<List<Alert>> GetAllAsync(string? severity = null, string? status = null)
    {
        try
        {
            var alerts = await _kagent.GetAlertsAsync();
            if (!string.IsNullOrEmpty(severity))
                alerts = alerts.Where(a => a.Severity == severity).ToList();
            if (!string.IsNullOrEmpty(status))
                alerts = alerts.Where(a => a.Status == status).ToList();
            return alerts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get alerts");
            return new List<Alert>();
        }
    }

    public Task<Alert?> GetByIdAsync(string id) => throw new NotImplementedException();
    public Task<Alert> CreateAsync(Alert alert) => throw new NotImplementedException();

    public async Task<bool> AcknowledgeAsync(string id) => await _kagent.AcknowledgeAlertAsync(id);
    public async Task<bool> ResolveAsync(string id) => await _kagent.ResolveAlertAsync(id);

    public async Task<AlertSummary> GetStatsAsync()
    {
        try { return await _kagent.GetAlertSummaryAsync(); }
        catch
        {
            var alerts = await GetAllAsync();
            return new AlertSummary
            {
                Total = alerts.Count,
                Firing = alerts.Count(a => a.Status == "firing"),
                Acknowledged = alerts.Count(a => a.Status == "acknowledged"),
                Resolved = alerts.Count(a => a.Status == "resolved"),
                BySeverity = new AlertSeverityBreakdown
                {
                    Critical = alerts.Count(a => a.Severity == "critical"),
                    High = alerts.Count(a => a.Severity == "high"),
                    Medium = alerts.Count(a => a.Severity == "medium"),
                    Low = alerts.Count(a => a.Severity == "low")
                }
            };
        }
    }
}
