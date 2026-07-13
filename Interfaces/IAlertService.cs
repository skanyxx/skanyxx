using SkanyxxWeb.Models;

namespace SkanyxxWeb.Interfaces;

public interface IAlertService
{
    Task<List<Alert>> GetAllAsync(string? severity = null, string? status = null);
    Task<Alert?> GetByIdAsync(string id);
    Task<Alert> CreateAsync(Alert alert);
    Task<bool> AcknowledgeAsync(string id);
    Task<bool> ResolveAsync(string id);
    Task<AlertSummary> GetStatsAsync();
}
