using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>Whether a person must approve a stage before the run moves on.</summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<Gate>))]
public enum Gate
{
    None,
    Human
}
