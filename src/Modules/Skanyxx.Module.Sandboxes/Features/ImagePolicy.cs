using System.Text.RegularExpressions;

namespace Skanyxx.Module.Sandboxes.Features;

/// <summary><c>Sandboxes:AllowedImages</c> + <c>RequireDigest</c>. No configured prefix means nothing may run.</summary>
internal static partial class ImagePolicy
{
    public static bool IsAllowed(string image, SandboxesOptions options) =>
        !image.Contains("..")
        && (!options.RequireDigest || Digest().IsMatch(image))
        && options.AllowedImages.Any(prefix => Matches(image, prefix));

    /// <summary>
    /// A prefix without a trailing '/' must end at a path, tag or digest boundary: <c>ghcr.io/acme</c> does not admit
    /// <c>ghcr.io/acme-evil/x</c>. A ':' is a tag only with no '/' after it — otherwise it is a registry port, and
    /// <c>ghcr.io</c> would admit <c>ghcr.io:5000/evil/x</c>.
    /// </summary>
    private static bool Matches(string image, string prefix) =>
        image.StartsWith(prefix, StringComparison.Ordinal)
        && (prefix.EndsWith('/') || image.Length == prefix.Length || image[prefix.Length] switch
        {
            '/' or '@' => true,
            ':' => image.IndexOf('/', prefix.Length) < 0,
            _ => false
        });

    [GeneratedRegex(@"@sha256:[a-f0-9]{64}\z")]
    private static partial Regex Digest();
}
