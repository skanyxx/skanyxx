using System.Globalization;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Runtime.Models.Definition;

/// <summary>
/// Reads and edits kagent's ModelConfig JSON (<c>{ref, spec, status}</c>, kagent 0.10.2). kagent's update replaces the
/// whole spec, so an edit starts from the current spec and touches only what the form owns: provider, model, the provider
/// block's form fields and the API-key reference. Everything else (TLS, headers, nested provider settings such as Ollama's
/// <c>options</c>) survives an edit on the same provider and is named in <see cref="ModelConfigDetail.Kept"/>; a provider
/// change starts clean, because kagent refuses a block that does not match the provider (D099, D106).
/// </summary>
internal static class ModelConfigSpec
{
    private static readonly HashSet<string> Owned = ["provider", "model", "apiKeySecret", "apiKeySecretKey", "apiKeyPassthrough"];

    private static readonly string[] KeyFields = ["apiKeySecret", "apiKeySecretKey", "apiKeyPassthrough"];

    public static ModelConfigDetail Read(JsonObject resource, string defaultRef)
    {
        var configRef = String(resource["ref"]) ?? "";
        var (ns, name) = ModelRefs.Split(configRef);
        var spec = resource["spec"] as JsonObject ?? [];
        // kagent's CRD defaults the provider to OpenAI.
        var provider = String(spec["provider"]) ?? "OpenAI";
        var schema = ProviderCatalog.All.GetValueOrDefault(provider);
        var block = schema is null ? null : spec[schema.Block] as JsonObject;
        var editable = schema?.Fields.Select(f => f.Key).ToHashSet() ?? [];
        var accepted = (resource["status"]?["conditions"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(c => String(c["type"]) == "Accepted");
        return new ModelConfigDetail(ns, name, provider, String(spec["model"]) ?? "", HasKey(spec),
            accepted is not null && String(accepted["status"]) == "True",
            accepted is null ? null : String(accepted["message"]),
            configRef == defaultRef,
            (block ?? []).Where(p => editable.Contains(p.Key) && Text(p.Value) is not null).ToDictionary(p => p.Key, p => Text(p.Value)!),
            [
                .. spec.Where(p => !Owned.Contains(p.Key) && p.Key != schema?.Block).Select(p => p.Key),
                .. (block ?? []).Where(p => !editable.Contains(p.Key)).Select(p => $"{schema!.Block}.{p.Key}")
            ]);
    }

    public static bool HasKey(JsonObject spec) =>
        !string.IsNullOrEmpty(String(spec["apiKeySecret"])) || spec["apiKeyPassthrough"] is JsonValue v && v.TryGetValue<bool>(out var on) && on;

    /// <summary>
    /// Keeping the stored key while an endpoint changes would send that key to the new host, so a new endpoint on a keyed
    /// provider needs the key pasted again (the owner confirms where it goes). <paramref name="keyReplaced"/>: a new key, or
    /// none at all — the stored one goes nowhere.
    /// </summary>
    public static bool RetargetsStoredKey(JsonObject? current, string provider, ProviderSchema schema, IReadOnlyDictionary<string, JsonNode> values, bool keyReplaced) =>
        !keyReplaced
        && schema.TakesKey
        && current is not null
        && String(current["provider"]) == provider
        && HasKey(current)
        && schema.Endpoints.Any(e => Text(current[schema.Block]?[e]) != (values.TryGetValue(e, out var v) ? Text(v) : null));

    /// <summary>
    /// A new key for another provider than the one whose key kagent's Secret holds. kagent merges a new key into that
    /// Secret beside the old one (<c>StringData</c>) but deletes an owned Secret the spec stops naming — so the switch is
    /// saved once without a key (the old Secret goes) and then with the new key (a fresh Secret).
    /// </summary>
    public static bool LeavesAStaleKey(JsonObject? current, string provider, bool newKey) =>
        newKey && current is not null && String(current["provider"]) != provider && !string.IsNullOrEmpty(String(current["apiKeySecret"]));

    /// <summary>
    /// The spec to send: <paramref name="values"/> (typed) replace the block's form fields, every other field stays.
    /// <paramref name="dropKey"/> removes the key reference — for a new key (kagent names the Secret after the
    /// ModelConfig and fills both fields itself, but only when the spec names none) or when no key is wanted.
    /// </summary>
    public static JsonObject Edit(JsonObject? current, string provider, ProviderSchema schema, string model, IReadOnlyDictionary<string, JsonNode> values, bool dropKey)
    {
        var spec = current is not null && String(current["provider"]) == provider ? (JsonObject)current.DeepClone() : [];
        spec["provider"] = provider;
        spec["model"] = model;

        var block = spec[schema.Block] as JsonObject ?? [];
        foreach (var field in schema.Fields)
        {
            if (values.TryGetValue(field.Key, out var value))
                block[field.Key] = value.DeepClone();
            else
                block.Remove(field.Key);
        }
        if (block.Count == 0)
            spec.Remove(schema.Block);
        else if (block.Parent is null)
            spec[schema.Block] = block;

        if (dropKey || !schema.TakesKey)
            foreach (var field in KeyFields)
                spec.Remove(field);
        return spec;
    }

    /// <summary>A form value as kagent decodes it; null when it is not of the field's kind.</summary>
    public static JsonNode? Typed(ParamField field, string value) => field.Kind switch
    {
        ParamKinds.Integer => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number) : null,
        ParamKinds.Boolean => value switch { "true" => JsonValue.Create(true), "false" => JsonValue.Create(false), _ => null },
        _ => JsonValue.Create(value)
    };

    /// <summary>The endpoint fields' values in <paramref name="spec"/>, in <see cref="ProviderSchema.Endpoints"/> order.</summary>
    public static IReadOnlyList<string?> Endpoints(JsonObject? spec, ProviderSchema schema) => [.. schema.Endpoints.Select(e => Text(spec?[schema.Block]?[e]))];

    /// <summary>
    /// Each endpoint field's origin for the audit: scheme, host and port, never a path or query; null when unset. Only for
    /// a spec <see cref="Edit"/> built, whose endpoints <see cref="ModelParams"/> checked are absolute http(s) URLs.
    /// </summary>
    public static Dictionary<string, string?> EndpointOrigins(JsonObject spec, ProviderSchema schema) =>
        schema.Endpoints.Zip(Endpoints(spec, schema)).ToDictionary(e => e.First, e => e.Second is null
            ? null
            : new Uri(e.Second).GetComponents(UriComponents.SchemeAndServer | UriComponents.Port, UriFormat.UriEscaped));

    public static string? String(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // A scalar as the form shows it; null for objects and lists.
    private static string? Text(JsonNode? node) => node is not JsonValue value
        ? null
        : value.TryGetValue<string>(out var s) ? s
        : value.TryGetValue<bool>(out var b) ? (b ? "true" : "false")
        : value.ToJsonString();
}
