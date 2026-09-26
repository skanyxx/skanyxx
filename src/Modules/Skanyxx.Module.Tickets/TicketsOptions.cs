using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Skanyxx.Module.Tickets;

public sealed class TicketsOptions
{
    public const string Section = "Tickets";

    /// <summary>Bound from <c>ConnectionStrings:Tickets</c>.</summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary><c>jira</c> or <c>local</c> (a JSON file of tickets, for demos and tests).</summary>
    [RegularExpression("^(jira|local)$")]
    public string Source { get; set; } = "jira";

    public string LocalPath { get; set; } = "";

    public JiraOptions Jira { get; set; } = new();

    /// <summary>One agent turn on a local model can take minutes.</summary>
    [Range(10, 3600)]
    public int StageTimeoutSeconds { get; set; } = 600;

    /// <summary>Sent to kagent as <c>X-User-Id</c>, so stage sessions are grouped under one service identity.</summary>
    [Required]
    public string KAgentUserId { get; set; } = "skanyxx-tickets";

    [Range(1, 3600)]
    public int PollSeconds { get; set; } = 5;

    [Range(1_000, 1_000_000)]
    public int MaxTicketChars { get; set; } = 20_000;

    /// <summary>Sized for the ticket, a whole prior stage (20k) and a rejection quoting it.</summary>
    [Range(8_000, 1_000_000)]
    public int MaxPromptChars { get; set; } = 96_000;

    /// <summary>An agent answer longer than this is cut (and says so). Stored in full it would inflate every later prompt and row.</summary>
    [Range(1_000, 1_000_000)]
    public int MaxAnswerChars { get; set; } = 100_000;

    /// <summary>Total stage attempts one run may make across all loops (20 stages × 10 loops would otherwise be ~200).</summary>
    [Range(1, 1_000)]
    public int MaxStageAttemptsPerRun { get; set; } = 30;

    /// <summary>Unfinished runs (pending, running or at a gate) one user may have at once; more is a 429.</summary>
    [Range(1, 1_000)]
    public int MaxActiveRunsPerUser { get; set; } = 5;

    /// <summary>Unfinished runs from everyone together; more is a 429. The per-user cap alone trusts a spoofable header.</summary>
    [Range(1, 10_000)]
    public int MaxActiveRuns { get; set; } = 20;

    /// <summary>Connections in tickets' own Npgsql pool (distinct from memory's by its Application Name).</summary>
    [Range(1, 1_000)]
    public int MaxPoolSize { get; set; } = 40;

    /// <summary>
    /// The kagent agents (<c>namespace/name</c>) a stage may call — exact refs, not namespaces, because kagent's own
    /// agents with cluster tools share the <c>kagent</c> namespace. Empty means the five ticket agents. A configured
    /// list replaces that default (the config binder would append to a non-empty default, so it starts empty).
    /// </summary>
    public string[] AllowedAgents { get; set; } = [];

    /// <summary>Users who may edit pipelines, and cancel or decide anyone's run. TODO(identity-slice): roles.</summary>
    public string[] Supervisors { get; set; } = [];

    /// <summary>
    /// The configured string with this module's pool settings. Npgsql keys pools by connection string, so the
    /// module-suffixed Application Name keeps tickets and memory from sharing (and exhausting) one pool, even when both
    /// connection strings are identical. A configured name (e.g. per replica) is kept as the prefix.
    /// </summary>
    internal NpgsqlConnectionStringBuilder ConnectionSettings()
    {
        var settings = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = MaxPoolSize };
        settings.ApplicationName = $"{(string.IsNullOrEmpty(settings.ApplicationName) ? "skanyxx" : settings.ApplicationName)}-tickets";
        return settings;
    }
}
