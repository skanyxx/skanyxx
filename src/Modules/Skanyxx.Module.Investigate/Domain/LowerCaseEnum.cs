using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skanyxx.Module.Investigate.Domain;

/// <summary>Enums travel as their snake_case names (<c>parallel</c>, <c>succeeded</c>), the spelling the page and the database use.</summary>
public sealed class LowerCaseEnum<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)
    where T : struct, Enum;
