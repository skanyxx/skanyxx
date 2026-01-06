using SkanyxxWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Models;
using SkanyxxWeb.Services;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
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
