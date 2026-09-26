using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>One step of a pipeline. Stored as data and snapshotted into every run.</summary>
public sealed record PipelineStage
{
    public required string Id { get; init; }
    public required StageKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Extra instructions for this stage; the role itself is the kagent agent's system message.</summary>
    public string Instructions { get; init; } = "";

    public IReadOnlyList<string> Skills { get; init; } = [];

    /// <summary>Run in order; each is handed the previous one's answer. The stage's output is the last one's.</summary>
    public IReadOnlyList<StageAgent> Agents { get; init; } = [];

    public Gate Gate { get; init; } = Gate.None;
    public ContextMode Context { get; init; } = ContextMode.All;

    /// <summary>Opt-in: without it a FAIL verdict is recorded and shown, and nothing else.</summary>
    public bool FailOnVerdict { get; init; }

    public OnFail OnFail { get; init; } = OnFail.Stop;
    public string? Goto { get; init; }

    /// <summary>How many times THIS stage may send the run backwards. Required with <c>on_fail: goto</c>; never implied.</summary>
    public int? MaxLoops { get; init; }

    [JsonIgnore]
    public string Name => Title.Length > 0 ? Title : Id;
}
