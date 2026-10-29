using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Diagnostics;

/// <summary>
/// The configuration for the Debug page (D353). Only Skanyxx's own sections are listed (the environment provider
/// also holds PATH, HOME and whatever the platform injects). A value is <see cref="SecretText.Mask"/> when its key
/// names a secret (password, secret, token, key, credential, certificate), it is a connection string, or the value
/// itself carries a credential (<c>password=</c>, URL user info, a token shape); everything else still goes through
/// <see cref="SecretText.Redact"/>.
/// </summary>
public static partial class ConfigurationView
{
    public static readonly IReadOnlyList<string> Sections =
    [
        "Skanyxx", "Identity", "Memory", "KAgent", "Sandboxes", "Studio", "Chat", "ChatContext", "Modules", "Tickets", "Analytics",
        "Plugins", "Features", "ConnectionStrings", "Logging", "Serilog", "Kestrel", "AllowedHosts", "urls", "environment",
        "OTEL_EXPORTER_OTLP_ENDPOINT"
    ];

    public static IReadOnlyList<ConfigSource> Sources(IConfigurationRoot root) =>
        [.. root.Providers.Select(p => new ConfigSource(Name(p), Keys(p).Count))];

    public static IReadOnlyList<ConfigRow> Rows(IConfigurationRoot root)
    {
        var providers = root.Providers.Reverse().ToList();
        return [.. root.AsEnumerable()
            .Where(kv => kv.Value is not null && Sections.Contains(kv.Key.Split(':')[0], StringComparer.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ConfigRow(kv.Key, Display(kv.Key, kv.Value!),
                providers.FirstOrDefault(p => p.TryGet(kv.Key, out _)) is { } p ? Name(p) : ""))];
    }

    public static string Display(string key, string value) =>
        IsSecretKey(key) || IsSecretValue(value) ? SecretText.Mask : SecretText.Redact(value);

    public static bool IsSecretKey(string key) =>
        key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) || SecretName().IsMatch(key);

    private static bool IsSecretValue(string value) =>
        SecretText.HasCredential(value) || value.Contains("password=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("pwd=", StringComparison.OrdinalIgnoreCase);

    private static string Name(IConfigurationProvider provider) => provider.ToString() ?? provider.GetType().Name;

    private static List<string> Keys(IConfigurationProvider provider, string? parent = null)
    {
        var keys = new List<string>();
        foreach (var child in provider.GetChildKeys([], parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = parent is null ? child : $"{parent}:{child}";
            if (provider.TryGet(path, out _))
                keys.Add(path);
            keys.AddRange(Keys(provider, path));
        }
        return keys;
    }

    [GeneratedRegex("pass|pwd|secret|token|key|credential|certificate|pfx|pat\b|auth|cookie|private|signing", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();
}
