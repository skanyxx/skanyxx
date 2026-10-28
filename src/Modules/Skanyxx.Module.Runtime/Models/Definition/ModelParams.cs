using System.Text.Json.Nodes;

namespace Skanyxx.Module.Runtime.Models.Definition;

/// <summary>
/// The form's provider fields against the provider: each typed as the CRD declares it. A field the form does not own,
/// a value of the wrong kind or a missing required field is refused — never dropped on the way to kagent.
/// </summary>
internal static class ModelParams
{
    private static readonly string[] UrlSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps];

    public static (IReadOnlyDictionary<string, JsonNode> Values, IReadOnlyList<string> Problems) Check(ProviderChoice provider, IReadOnlyDictionary<string, string> form)
    {
        var values = new Dictionary<string, JsonNode>();
        var problems = new List<string>();
        foreach (var (key, raw) in form)
        {
            var field = provider.Schema.Fields.FirstOrDefault(f => f.Key == key);
            if (field is null)
            {
                problems.Add($"'{key}' is not a {provider.Type} field this form sets; change it where the model was made.");
                continue;
            }
            if (raw.Length == 0)
                continue;
            if (ModelConfigSpec.Typed(field, raw) is not { } value)
                problems.Add(field.Kind == ParamKinds.Integer ? $"'{key}' must be a whole number." : $"'{key}' must be true or false.");
            else if (provider.Schema.Endpoints.Contains(key) && !IsHttpUrl(raw))
                problems.Add($"'{key}' must be an absolute http(s) URL without user info, query or fragment.");
            else
                values[key] = value;
        }
        problems.AddRange(provider.Required.Where(r => !values.ContainsKey(r)).Order(StringComparer.Ordinal)
            .Select(r => $"'{r}' is required for {provider.Type}."));
        return (values, problems);
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && UrlSchemes.Contains(uri.Scheme)
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && url == url.Trim();
}
