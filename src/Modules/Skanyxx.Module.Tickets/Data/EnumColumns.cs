using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skanyxx.Module.Tickets.Data;

/// <summary>Enums are stored as their snake_case names, the same spelling the API uses.</summary>
internal static class EnumColumns
{
    public static PropertyBuilder<T> AsName<T>(this PropertyBuilder<T> property) where T : struct, Enum =>
        property.HasMaxLength(32).HasConversion(
            v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()),
            v => Enum.Parse<T>(v.Replace("_", ""), true));
}
