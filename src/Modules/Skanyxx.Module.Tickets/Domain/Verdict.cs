using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>Closed set: what a stage SAID about the work. Unparseable is Unknown, never a guess.</summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<Verdict>))]
public enum Verdict
{
    Pass,
    Fail,
    Unknown
}
