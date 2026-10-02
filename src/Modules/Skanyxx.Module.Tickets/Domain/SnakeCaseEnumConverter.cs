using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skanyxx.Module.Tickets.Domain;

/// <summary>Enums travel as <c>snake_case</c> names (API and jsonb alike); numbers are refused.</summary>
public sealed class SnakeCaseEnumConverter<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)
    where T : struct, Enum;
