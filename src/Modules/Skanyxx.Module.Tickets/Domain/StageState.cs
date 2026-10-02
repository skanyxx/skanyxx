using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>
/// Closed set: what the run DID with a stage attempt (not what the stage said — see Verdict). A stage an agent is
/// working on has no row yet; the run reports it as <c>runningStageId</c>. Skipped = cancelled at a gate.
/// </summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<StageState>))]
public enum StageState
{
    AwaitingHuman,
    Passed,
    Failed,
    Skipped
}
