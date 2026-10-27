using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skanyxx.Module.ChatContext.Data;

/// <summary>Maps a property to a jsonb column through System.Text.Json, compared by its serialized form (as tickets does).</summary>
internal static class JsonColumn
{
    public static PropertyBuilder<T> HasJson<T>(this PropertyBuilder<T> property) =>
        property.HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v),
                v => JsonSerializer.Deserialize<T>(v)!,
                new ValueComparer<T>(
                    (a, b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b),
                    v => JsonSerializer.Serialize(v).GetHashCode(),
                    v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v))!));
}
