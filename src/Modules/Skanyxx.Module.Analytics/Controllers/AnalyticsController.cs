using Skanyxx.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;

namespace Skanyxx.Module.Analytics.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    // Unified endpoint for Analytics page
    [HttpGet]
    public async Task<ActionResult<object>> GetAll([FromQuery] string range = "7d")
    {
        var stats = await _analyticsService.GetStatsAsync();
        var sessionAnalytics = await _analyticsService.GetSessionAnalyticsAsync(range);
        var toolUsage = await _analyticsService.GetToolUsageStatsAsync(range);
        var recentSessions = await _analyticsService.GetRecentSessionsAsync(10);

        return new
        {
            totalEvents = sessionAnalytics.TotalMessages,
            avgResponseTime = stats.AvgResponseTimeMs,
            successRate = stats.SuccessRate,
            activeSessions = sessionAnalytics.TotalSessions,
            eventsChange = sessionAnalytics.MessagesChangePercent != 0
                ? $"{(sessionAnalytics.MessagesChangePercent > 0 ? "+" : "")}{sessionAnalytics.MessagesChangePercent}%"
                : "-",
            responseChange = "-",
            successChange = "-",
            sessionsChange = sessionAnalytics.SessionsChangePercent != 0
                ? $"{(sessionAnalytics.SessionsChangePercent > 0 ? "+" : "")}{sessionAnalytics.SessionsChangePercent}%"
                : "-",
            chartData = new int[] { }, // Would need time-series data
            eventTypes = toolUsage.Select(t => new {
                type = t.ToolName,
                label = t.ToolName,
                count = t.UsageCount
            }).ToList(),
            sessions = recentSessions.Select(s => new {
                id = s.Id,
                agent = s.AgentName,
                events = s.MessageCount,
                duration = 0, // Calculate from LastActivity
                status = "active"
            }).ToList()
        };
    }

    [HttpGet("stats")]
    public async Task<ActionResult<AnalyticsData>> GetStats()
    {
        return await _analyticsService.GetStatsAsync();
    }

    [HttpGet("session-analytics")]
    public async Task<ActionResult<SessionAnalytics>> GetSessionAnalytics([FromQuery] string period = "7d")
    {
        return await _analyticsService.GetSessionAnalyticsAsync(period);
    }

    [HttpGet("tool-usage")]
    public async Task<ActionResult<List<ToolUsageStats>>> GetToolUsage([FromQuery] string period = "7d")
    {
        return await _analyticsService.GetToolUsageStatsAsync(period);
    }

    [HttpGet("recent-sessions")]
    public async Task<ActionResult<List<RecentSession>>> GetRecentSessions([FromQuery] int limit = 10)
    {
        return await _analyticsService.GetRecentSessionsAsync(limit);
    }

    [HttpGet("endpoints")]
    public async Task<ActionResult<List<EndpointMetrics>>> GetEndpointMetrics()
    {
        return await _analyticsService.GetEndpointMetricsAsync();
    }

    [HttpGet("errors")]
    public ActionResult GetErrorDistribution()
    {
        // Error tracking would require middleware
        return Ok(new object[] { });
    }

    [HttpGet("volume")]
    public ActionResult GetRequestVolume([FromQuery] string period = "24h")
    {
        // Request volume would require middleware
        return Ok(new object[] { });
    }
}
