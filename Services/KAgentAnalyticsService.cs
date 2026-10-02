using SkanyxxWeb.Interfaces;
using SkanyxxWeb.Models;

namespace SkanyxxWeb.Services;

public class KAgentAnalyticsService : IAnalyticsService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentAnalyticsService> _logger;

    public KAgentAnalyticsService(KAgentApiClient kagent, ILogger<KAgentAnalyticsService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<SessionAnalytics> GetSessionAnalyticsAsync(string period = "7d")
    {
        try
        {
            var sessions = await _kagent.GetSessionsAsync();
            var totalMessages = 0;
            var totalTokens = 0;
            var totalDuration = 0L;
            var toolsUsed = new HashSet<string>();

            foreach (var session in sessions)
            {
                try
                {
                    // Get events and tasks for each session
                    var events = await _kagent.GetSessionEventsAsync(session.Id);
                    var tasks = await _kagent.GetSessionTasksAsync(session.Id);

                    totalMessages += events.Count;

                    // Calculate tokens from tasks
                    foreach (var task in tasks)
                    {
                        if (task.Metadata?.KagentUsageMetadata?.TotalTokenCount > 0)
                        {
                            totalTokens += task.Metadata.KagentUsageMetadata.TotalTokenCount;
                        }

                        // Extract tools used
                        if (task.History != null)
                        {
                            foreach (var history in task.History.Where(h => h.Kind == "message"))
                            {
                                if (history.Parts != null)
                                {
                                    foreach (var part in history.Parts.Where(p => p.Kind == "data"))
                                    {
                                        if (part.Metadata?.KagentType == "function_call" && part.Data?.Name != null)
                                        {
                                            toolsUsed.Add(part.Data.Name);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Calculate duration
                    if (DateTime.TryParse(session.LastUpdateTime, out var lastUpdate))
                    {
                        var duration = (DateTime.UtcNow - lastUpdate).TotalMinutes;
                        totalDuration += (long)duration;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get analytics for session {SessionId}", session.Id);
                }
            }

            var avgDuration = sessions.Count > 0 ? totalDuration / sessions.Count : 0;

            return new SessionAnalytics
            {
                TotalSessions = sessions.Count,
                TotalMessages = totalMessages,
                TotalTokens = totalTokens,
                AvgDuration = FormatDuration(avgDuration),
                SessionsChangePercent = 0, // Would need historical data
                MessagesChangePercent = 0,
                TokensChangePercent = 0,
                DurationChangePercent = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session analytics");
            return new SessionAnalytics();
        }
    }

    public async Task<List<ToolUsageStats>> GetToolUsageStatsAsync(string period = "7d")
    {
        try
        {
            var sessions = await _kagent.GetSessionsAsync();
            var toolCounts = new Dictionary<string, int>();
            var totalCalls = 0;

            foreach (var session in sessions)
            {
                try
                {
                    var tasks = await _kagent.GetSessionTasksAsync(session.Id);

                    foreach (var task in tasks)
                    {
                        if (task.History != null)
                        {
                            foreach (var history in task.History.Where(h => h.Kind == "message"))
                            {
                                if (history.Parts != null)
                                {
                                    foreach (var part in history.Parts.Where(p => p.Kind == "data"))
                                    {
                                        if (part.Metadata?.KagentType == "function_call" && part.Data?.Name != null)
                                        {
                                            var toolName = part.Data.Name;
                                            toolCounts[toolName] = toolCounts.GetValueOrDefault(toolName) + 1;
                                            totalCalls++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return toolCounts
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv => new ToolUsageStats
                {
                    ToolName = kv.Key,
                    UsageCount = kv.Value,
                    Percentage = totalCalls > 0 ? Math.Round((decimal)kv.Value / totalCalls * 100, 1) : 0
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tool usage stats");
            return new List<ToolUsageStats>();
        }
    }

    public async Task<List<RecentSession>> GetRecentSessionsAsync(int limit = 10)
    {
        try
        {
            var sessions = await _kagent.GetSessionsAsync();
            var recentSessions = new List<RecentSession>();

            foreach (var session in sessions.Take(limit))
            {
                var messageCount = 0;
                try
                {
                    var events = await _kagent.GetSessionEventsAsync(session.Id);
                    messageCount = events.Count;
                }
                catch { }

                DateTime lastActivity = DateTime.UtcNow;
                if (DateTime.TryParse(session.LastUpdateTime, out var dt))
                {
                    lastActivity = dt;
                }

                recentSessions.Add(new RecentSession
                {
                    Id = session.Id,
                    Name = session.Name ?? $"Session {session.Id[..Math.Min(8, session.Id.Length)]}",
                    AgentName = session.AgentRef ?? "Unknown",
                    MessageCount = messageCount,
                    LastActivity = lastActivity,
                    Duration = FormatTimeAgo(lastActivity)
                });
            }

            return recentSessions.OrderByDescending(s => s.LastActivity).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent sessions");
            return new List<RecentSession>();
        }
    }

    public async Task<AnalyticsData> GetStatsAsync()
    {
        try
        {
            var sessions = await _kagent.GetSessionsAsync();
            var toolServers = await _kagent.GetToolServersAsync();

            return new AnalyticsData
            {
                TotalEvents = sessions.Count,
                AvgResponseTimeMs = 0,
                SuccessRate = 100,
                ActiveServices = toolServers.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get analytics stats");
            return new AnalyticsData();
        }
    }

    public Task<List<EndpointMetrics>> GetEndpointMetricsAsync()
    {
        return Task.FromResult(new List<EndpointMetrics>());
    }

    private static string FormatDuration(long minutes)
    {
        if (minutes < 60)
            return $"{minutes}m";
        var hours = minutes / 60;
        var mins = minutes % 60;
        return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
    }

    private static string FormatTimeAgo(DateTime time)
    {
        var diff = DateTime.UtcNow - time;
        if (diff.TotalMinutes < 1)
            return "Just now";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }
}
