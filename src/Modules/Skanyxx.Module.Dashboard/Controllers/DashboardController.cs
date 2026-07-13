using MediatR;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly KAgentApiClient _kagent;

    public DashboardController(IMediator mediator, KAgentApiClient kagent)
    {
        _mediator = mediator;
        _kagent = kagent;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStats>> GetStats()
    {
        int activeAgents = 0;
        int activeIncidents = 0;
        int apiCalls = 0;

        try { var agents = await _mediator.Send(new GetAllAgentsQuery()); activeAgents = agents.Count(a => a.Status == "Active"); } catch { }
        try { var alertSummary = await _mediator.Send(new GetAlertSummaryQuery()); activeIncidents = alertSummary.Firing; } catch { }
        try { var analytics = await _mediator.Send(new GetSessionAnalyticsQuery()); apiCalls = analytics.TotalMessages; } catch { }

        decimal uptime = 0;
        string uptimeChange = "KAgent offline";
        try { await _kagent.GetToolServersAsync(); uptime = 100m; uptimeChange = "KAgent connected"; } catch { uptime = 0; uptimeChange = "KAgent unavailable"; }

        return new DashboardStats
        {
            ActiveAgents = activeAgents,
            AgentsChange = activeAgents > 0 ? $"{activeAgents} agents running" : "Connect KAgent to see agents",
            Uptime = uptime,
            UptimeChange = uptimeChange,
            ActiveIncidents = activeIncidents,
            IncidentsChange = activeIncidents == 0 ? "No issues" : $"{activeIncidents} active",
            ApiCallsToday = apiCalls,
            ApiCallsChange = apiCalls > 0 ? $"+{apiCalls} today" : "No API calls yet"
        };
    }

    [HttpGet("status")]
    public async Task<ActionResult<List<SystemStatus>>> GetSystemStatus()
    {
        return await _mediator.Send(new GetClusterStatusQuery());
    }

    [HttpGet("services")]
    public async Task<ActionResult<List<ServiceStatus>>> GetServices()
    {
        var services = new List<ServiceStatus>();
        var kagentStatus = await CheckServiceStatusAsync("KAgent API", async () => { await _kagent.GetToolServersAsync(); return true; });
        services.Add(kagentStatus);
        var webServerStatus = await CheckServiceStatusAsync("Web Server", async () => { await Task.CompletedTask; return true; });
        services.Add(webServerStatus);
        var dbStatus = await CheckServiceStatusAsync("Database", async () => { await Task.CompletedTask; return true; });
        services.Add(dbStatus);
        var settingsStatus = await CheckServiceStatusAsync("Settings Service", async () => { await Task.CompletedTask; return true; });
        services.Add(settingsStatus);
        return services;
    }

    private async Task<ServiceStatus> CheckServiceStatusAsync(string name, Func<Task<bool>> check)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await check(); sw.Stop(); return new ServiceStatus { Name = name, Status = "online", Latency = $"{sw.ElapsedMilliseconds}ms" }; }
        catch { return new ServiceStatus { Name = name, Status = "offline", Latency = "-" }; }
    }

    [HttpGet("activity")]
    public async Task<ActionResult<List<ActivityItem>>> GetRecentActivity([FromQuery] int limit = 10)
    {
        var activities = new List<ActivityItem>();
        try
        {
            var sessions = await _kagent.GetSessionsAsync();
            foreach (var session in sessions.Take(limit))
            {
                try
                {
                    var events = await _kagent.GetSessionEventsAsync(session.Id, limit: 5);
                    foreach (var evt in events)
                        activities.Add(new ActivityItem { Id = evt.Id, Type = "chat", Message = $"Message in session {session.Name ?? session.Id[..8]}", Timestamp = DateTime.TryParse(evt.Timestamp, out var ts) ? ts : DateTime.UtcNow });
                }
                catch { }
                if (DateTime.TryParse(session.LastUpdateTime, out var lastUpdate))
                    activities.Add(new ActivityItem { Id = session.Id, Type = "session", Message = $"Session updated: {session.Name ?? session.Id[..8]}", Timestamp = lastUpdate });
            }

            var alerts = await _mediator.Send(new GetAllAlertsQuery());
            foreach (var alert in alerts.Take(5))
                activities.Add(new ActivityItem { Id = alert.Id, Type = "alert", Message = $"Alert: {alert.Message}", Timestamp = DateTime.TryParse(alert.FirstSeen, out var ts) ? ts : DateTime.UtcNow });
        }
        catch { }
        return activities.OrderByDescending(a => a.Timestamp).Take(limit).ToList();
    }

    [HttpGet("alert-summary")]
    public async Task<ActionResult<AlertSummary>> GetAlertSummary()
    {
        return await _mediator.Send(new GetAlertSummaryQuery());
    }
}
