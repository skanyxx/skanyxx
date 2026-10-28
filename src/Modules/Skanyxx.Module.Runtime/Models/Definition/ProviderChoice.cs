using System.Text.Json.Nodes;

namespace Skanyxx.Module.Runtime.Models.Definition;

/// <summary>A provider kagent supports and Skanyxx knows the fields of, with every field it requires (kagent's and Skanyxx's).</summary>
internal sealed record ProviderChoice(string Type, ProviderSchema Schema, IReadOnlySet<string> Required)
{
    /// <summary>kagent's provider list (<c>[{type, requiredParams}]</c>) narrowed to <see cref="ProviderCatalog"/>, in kagent's order.</summary>
    public static IReadOnlyList<ProviderChoice> Of(JsonArray providers) =>
    [
        .. providers.OfType<JsonObject>()
            .Select(p => (Type: ModelConfigSpec.String(p["type"]), Required: p["requiredParams"] as JsonArray ?? []))
            .Where(p => p.Type is not null && ProviderCatalog.All.ContainsKey(p.Type))
            .DistinctBy(p => p.Type)
            .Select(p => Choice(p.Type!, p.Required))
    ];

    // A field kagent requires that the form cannot set would refuse every save; kagent itself says so instead.
    private static ProviderChoice Choice(string type, JsonArray kagentRequired)
    {
        var schema = ProviderCatalog.All[type];
        return new ProviderChoice(type, schema, schema.Required
            .Concat(kagentRequired.Select(ModelConfigSpec.String).OfType<string>().Where(k => schema.Fields.Any(f => f.Key == k)))
            .ToHashSet());
    }
}
