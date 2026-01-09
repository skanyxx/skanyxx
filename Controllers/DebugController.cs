using Microsoft.AspNetCore.Mvc;
using SkanyxxWeb.Services;
using SkanyxxWeb.Interfaces;

namespace SkanyxxWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DebugController : ControllerBase
{
    private readonly KAgentApiClient _kagent;
    private readonly IAgentService _agentService;
    private readonly IAlertService _alertService;
    private readonly ILogger<DebugController> _logger;
    private static readonly List<LogEntry> _logs = new();
    private static readonly object _logsLock = new();
    private static DateTime _startTime = DateTime.UtcNow;

    public DebugController(
        KAgentApiClient kagent,
        IAgentService agentService,
        IAlertService alertService,
        ILogger<DebugController> logger)
    {
        _kagent = kagent;
        _agentService = agentService;
        _alertService = alertService;
        _logger = logger;
    }

    [HttpGet("logs")]
    public async Task<ActionResult<List<LogEntry>>> GetLogs([FromQuery] int limit = 50)
    {
        var logs = new List<LogEntry>();
        var now = DateTime.Now;

        // Check KAgent connection status
        bool kagentConnected = false;
        try
        {
            await _kagent.GetToolServersAsync();
            kagentConnected = true;
            logs.Add(new LogEntry
            {
                Level = "info",
                Time = now.ToString("HH:mm:ss"),
                Message = "KAgent API connection established"
            });
        }
        catch (Exception ex)
        {
            logs.Add(new LogEntry
            {
                Level = "error",
                Time = now.ToString("HH:mm:ss"),
                Message = $"KAgent API unavailable: {ex.Message}"
            });
        }

        // Get agents status
        try
        {
            var agents = await _agentService.GetAllAsync();
            var activeCount = agents.Count(a => a.Status == "Active");
            var inactiveCount = agents.Count - activeCount;

            if (agents.Any())
            {
                logs.Add(new LogEntry
                {
                    Level = "info",
                    Time = now.AddSeconds(-1).ToString("HH:mm:ss"),
                    Message = $"Loaded {agents.Count} agents ({activeCount} active, {inactiveCount} inactive)"
                });

                foreach (var agent in agents.Take(5))
                {
                    logs.Add(new LogEntry
                    {
                        Level = agent.Status == "Active" ? "info" : "warning",
                        Time = now.AddSeconds(-2).ToString("HH:mm:ss"),
                        Message = $"Agent '{agent.Name}' in namespace '{agent.Namespace}' - Status: {agent.Status}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logs.Add(new LogEntry
            {
                Level = "warning",
                Time = now.ToString("HH:mm:ss"),
                Message = $"Failed to load agents: {ex.Message}"
            });
        }

        // Get alerts
        try
        {
            var alerts = await _alertService.GetAllAsync();
            if (alerts.Any())
            {
                foreach (var alert in alerts.Take(5))
                {
                    logs.Add(new LogEntry
                    {
                        Level = alert.Severity?.ToLower() == "critical" ? "error" : "warning",
                        Time = alert.CreatedAt.ToString("HH:mm:ss"),
                        Message = $"Alert [{alert.Severity}]: {alert.Title}"
                    });
                }
            }
        }
        catch { }

        // Get recent sessions
        if (kagentConnected)
        {
            try
            {
                var sessions = await _kagent.GetSessionsAsync();
                foreach (var session in sessions.Take(5))
                {
                    if (DateTime.TryParse(session.LastUpdateTime, out var lastUpdate))
                    {
                        logs.Add(new LogEntry
                        {
                            Level = "debug",
                            Time = lastUpdate.ToString("HH:mm:ss"),
                            Message = $"Session '{session.Name ?? session.Id[..Math.Min(8, session.Id.Length)]}' - Agent: {session.AgentRef ?? "unknown"}"
                        });
                    }
                }
            }
            catch { }
        }

        // Add system uptime log
        var uptime = DateTime.UtcNow - _startTime;
        logs.Add(new LogEntry
        {
            Level = "info",
            Time = _startTime.ToLocalTime().ToString("HH:mm:ss"),
            Message = $"System started - Uptime: {FormatUptime(uptime)}"
        });

        // Add any manually stored logs
        lock (_logsLock)
        {
            logs.AddRange(_logs);
        }

        // Sort by time descending and return
        return logs
            .OrderByDescending(l => l.Time)
            .Take(limit)
            .ToList();
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalMinutes < 1) return $"{(int)uptime.TotalSeconds}s";
        if (uptime.TotalHours < 1) return $"{(int)uptime.TotalMinutes}m";
        if (uptime.TotalDays < 1) return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
    }

    [HttpPost("log")]
    public ActionResult AddLog([FromBody] LogEntry entry)
    {
        lock (_logsLock)
        {
            entry.Time = DateTime.Now.ToString("HH:mm:ss");
            _logs.Insert(0, entry);
            if (_logs.Count > 100)
                _logs.RemoveAt(_logs.Count - 1);
        }
        return Ok();
    }

    public static void Log(string level, string message)
    {
        lock (_logsLock)
        {
            _logs.Insert(0, new LogEntry
            {
                Level = level,
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Message = message
            });
            if (_logs.Count > 100)
                _logs.RemoveAt(_logs.Count - 1);
        }
    }
}

public class LogEntry
{
    public string Level { get; set; } = "info";
    public string Time { get; set; } = "";
    public string Message { get; set; } = "";
}
