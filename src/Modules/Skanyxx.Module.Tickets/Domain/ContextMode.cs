using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>Which earlier stages a stage is shown: all of them, the last one, or none (ticket only).</summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<ContextMode>))]
public enum ContextMode
{
    All,
    Last,
    None
}
