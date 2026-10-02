using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>What a failed stage does to the run. Goto needs an earlier target and MaxLoops.</summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<OnFail>))]
public enum OnFail
{
    Stop,
    Continue,
    Goto
}
