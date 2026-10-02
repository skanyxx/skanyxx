using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skanyxx.Module.Tickets.Data;

/// <summary>Maps a property to a jsonb column through System.Text.Json, compared by its serialized form.</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<T> HasJson<T>(this PropertyBuilder<T> property) =>
        property.HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Options),
                v => JsonSerializer.Deserialize<T>(v, Options)!,
                new ValueComparer<T>(
                    (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                    v => JsonSerializer.Serialize(v, Options).GetHashCode(),
                    v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!));
}
