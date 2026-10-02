using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>Closed set of stage kinds; each maps to a role, a verdict rule and a fallback.</summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<StageKind>))]
public enum StageKind
{
    Plan,
    Review,
    Code,
    Qa
}
