using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Skanyxx.Module.Investigate;

public sealed class InvestigateOptions
{
    public const string Section = "Investigate";

    /// <summary>Bound from <c>ConnectionStrings:Memory</c>: workflows and runs live in memory's database, under their own migrations.</summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>One agent turn on a local model can take minutes; a step that takes longer fails the run.</summary>
    [Range(5, 3600)]
    public int StepTimeoutSeconds { get; set; } = 300;

    /// <summary>Sent to kagent as <c>X-User-Id</c>, so investigation sessions are grouped under one service identity.</summary>
    [Required, RegularExpression("^[a-z0-9][a-z0-9._@-]{0,127}$")]
    public string KAgentUserId { get; set; } = "skanyxx-investigate";

    /// <summary>Levels of nesting: top-level steps are level 1, a group's steps one deeper.</summary>
    [Range(1, 8)]
    public int MaxDepth { get; set; } = 4;

    /// <summary>Steps in one workflow, groups included.</summary>
    [Range(1, 200)]
    public int MaxSteps { get; set; } = 40;

    [Range(100, 100_000)]
    public int MaxPromptChars { get; set; } = 8_000;

    [Range(100, 200_000)]
    public int MaxInputChars { get; set; } = 20_000;

    /// <summary>
    /// A rendered prompt's ceiling (template, input and earlier outputs). Over it, each substituted value is cut inside its
    /// own envelope and says so; the template and the markers are never cut (D389).
    /// </summary>
    [Range(1_000, 1_000_000)]
    public int MaxRenderedPromptChars { get; set; } = 100_000;

    /// <summary>An agent's answer longer than this is cut (and says so).</summary>
    [Range(1_000, 1_000_000)]
    public int MaxOutputChars { get; set; } = 50_000;

    /// <summary>Unfinished runs (queued or running) one person may have; more is a 429.</summary>
    [Range(1, 1_000)]
    public int MaxActiveRunsPerUser { get; set; } = 3;

    /// <summary>Unfinished runs from everyone together; more is a 429.</summary>
    [Range(1, 10_000)]
    public int MaxActiveRuns { get; set; } = 20;

    /// <summary>Runs one replica executes at the same time; the rest wait in the queue.</summary>
    [Range(1, 100)]
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>How often the worker looks for queued runs without being woken.</summary>
    [Range(1, 3600)]
    public int PollSeconds { get; set; } = 5;

    /// <summary>How often a running run checks for a cancel and stamps its heartbeat.</summary>
    [Range(50, 60_000)]
    public int WatchMilliseconds { get; set; } = 1_000;

    /// <summary>A running run whose heartbeat is older than this lost its replica and is failed.</summary>
    [Range(10, 3600)]
    public int StaleAfterSeconds { get; set; } = 120;

    [Range(1, 1_000)]
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>Agent steps of one run that call kagent at the same time; the rest of a parallel group waits its turn.</summary>
    [Range(1, 50)]
    public int MaxParallelSteps { get; set; } = 4;

    /// <summary>Workflows one person may own; creating or duplicating past it is a 409.</summary>
    [Range(1, 10_000)]
    public int MaxWorkflowsPerUser { get; set; } = 100;

    /// <summary>
    /// MCP tool servers (RemoteMCPServer names) whose tools only read (D372). An agent naming any other server is
    /// write-capable, so a step reading an earlier answer may call it only on a workflow that allows chained writes.
    /// </summary>
    public List<string> ReadOnlyToolServers { get; set; } = [];

    internal NpgsqlConnectionStringBuilder ConnectionSettings()
    {
        var settings = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = MaxPoolSize };
        settings.ApplicationName = $"{(string.IsNullOrEmpty(settings.ApplicationName) ? "skanyxx" : settings.ApplicationName)}-investigate";
        return settings;
    }
}
