using System.Text.Json.Serialization;

namespace Skanyxx.Module.Investigate.Domain;

/// <summary><see cref="Skipped"/>: never started, because the run ended before it was reached.</summary>
[JsonConverter(typeof(LowerCaseEnum<StepState>))]
public enum StepState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Skipped
}
