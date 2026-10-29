using System.Text.Json.Serialization;

namespace Skanyxx.Module.Investigate.Domain;

[JsonConverter(typeof(LowerCaseEnum<RunState>))]
public enum RunState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}
