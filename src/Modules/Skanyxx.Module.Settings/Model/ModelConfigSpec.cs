using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Settings.Model;

/// <summary>
/// Reads and edits kagent's ModelConfig JSON (<c>{ref, spec, status}</c>, kagent 0.10.2). kagent's update replaces the
/// whole spec, so an edit starts from the current spec and touches only what the model step owns: provider, model, the
/// provider block's URL and the API-key reference. Everything else on the same provider (e.g. Ollama's <c>num_ctx</c>)
/// survives; a provider change starts clean, because kagent refuses a block that does not match the provider.
/// </summary>
internal static class ModelConfigSpec
{
    public static ModelSettings Read(string configRef, JsonObject? data)
    {
        if (data?["spec"] is not JsonObject spec)
            return new ModelSettings(configRef, null, null, null, false, false, null);

        var provider = String(spec["provider"]) ?? ModelProviders.OpenAI;
        var accepted = (data["status"]?["conditions"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(c => String(c["type"]) == "Accepted");
        return new ModelSettings(
            String(data["ref"]) ?? configRef,
            provider,
            String(spec["model"]),
            String(spec[Block(provider)]?[UrlField(provider)]),
            HasKey(spec),
            accepted is not null && String(accepted["status"]) == "True",
            accepted is null ? null : String(accepted["message"]));
    }

    public static bool HasKey(JsonObject spec) =>
        !string.IsNullOrEmpty(String(spec["apiKeySecret"])) || spec["apiKeyPassthrough"] is JsonValue v && v.TryGetValue<bool>(out var on) && on;

    /// <summary>
    /// Keeping the stored key while the endpoint changes would send that key to the new host, so a new base URL on a
    /// keyed provider needs the key pasted again (the owner confirms where it goes).
    /// </summary>
    public static bool RetargetsStoredKey(JsonObject? current, string provider, string? baseUrl, bool newKey) =>
        !newKey
        && provider != ModelProviders.Ollama
        && current is not null
        && String(current["provider"]) == provider
        && HasKey(current)
        && String(current[Block(provider)]?[UrlField(provider)]) != (string.IsNullOrEmpty(baseUrl) ? null : baseUrl);

    /// <summary>
    /// A new key for a different provider than the one whose key kagent's Secret holds. kagent merges a new key into
    /// that Secret beside the old one (<c>StringData</c>), but deletes an owned Secret the spec stops naming — so the
    /// switch is saved once without a key (the old Secret goes) and then with the new key (a fresh Secret).
    /// </summary>
    public static bool LeavesAStaleKey(JsonObject? current, string provider, bool newKey) =>
        newKey && current is not null && String(current["provider"]) != provider && !string.IsNullOrEmpty(String(current["apiKeySecret"]));

    /// <summary>The spec to send; null when a keyed provider would be left without a key.</summary>
    public static JsonObject? Edit(JsonObject? current, string provider, string model, string? baseUrl, bool newKey)
    {
        var spec = current is not null && String(current["provider"]) == provider ? (JsonObject)current.DeepClone() : [];
        spec["provider"] = provider;
        spec["model"] = model;

        var block = spec[Block(provider)] as JsonObject ?? [];
        if (string.IsNullOrEmpty(baseUrl))
            block.Remove(UrlField(provider));
        else
            block[UrlField(provider)] = baseUrl;
        if (block.Count > 0)
            spec[Block(provider)] = block;
        else
            spec.Remove(Block(provider));

        // A new key: kagent names the Secret after the ModelConfig and fills both fields itself, but only when the spec
        // names no secret. Ollama takes none.
        if (provider == ModelProviders.Ollama || newKey)
        {
            spec.Remove("apiKeySecret");
            spec.Remove("apiKeySecretKey");
            spec.Remove("apiKeyPassthrough");
            return spec;
        }
        return HasKey(spec) ? spec : null;
    }

    private static string Block(string provider) => provider switch
    {
        ModelProviders.Ollama => "ollama",
        ModelProviders.Anthropic => "anthropic",
        _ => "openAI"
    };

    private static string UrlField(string provider) => provider == ModelProviders.Ollama ? "host" : "baseUrl";

    private static string? String(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
