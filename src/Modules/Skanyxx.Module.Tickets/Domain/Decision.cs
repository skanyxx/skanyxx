using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>A person's answer at a human gate.</summary>
[JsonConverter(typeof(SnakeCaseEnumConverter<Decision>))]
public enum Decision
{
    Approve,
    Reject
}
