using System.Text.Json.Serialization;

namespace Skanyxx.Core.Platform.Studio;

[JsonConverter(typeof(JsonStringEnumConverter<StepState>))]
public enum StepState
{
    Done,
    Active,
    Failed,
    Waiting,
    Skipped
}
