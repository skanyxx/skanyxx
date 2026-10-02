using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface IAnalyticsService
{
    Task<SessionAnalytics> GetSessionAnalyticsAsync(string period = "7d");
    Task<List<ToolUsageStats>> GetToolUsageStatsAsync(string period = "7d");
    Task<List<RecentSession>> GetRecentSessionsAsync(int limit = 10);
    Task<AnalyticsData> GetStatsAsync();
    Task<List<EndpointMetrics>> GetEndpointMetricsAsync();
}
