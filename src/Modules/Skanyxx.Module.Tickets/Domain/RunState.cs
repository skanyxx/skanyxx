using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>Closed set: what a run is doing.</summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<RunState>))]
public enum RunState
{
    Pending,
    Running,
    AwaitingHuman,
    Succeeded,
    Failed,
    Cancelled
}
