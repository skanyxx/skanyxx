using System.Net;
using System.Text.RegularExpressions;
using Skanyxx.Module.Agents.Plugins.Sources;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// What a plugin MCP server's URL may be, on the install and on the files of a proposal and of main alike. It goes to git
/// as it is and kagent connects to it from inside the cluster with the owner's header values, so it carries no
/// credential (user info, a key-like query parameter, a long random path segment, a fragment, anything
/// <see cref="SecretScan"/> knows), its host is plain ASCII, and it names no internal address unless the owner allowed it
/// (<c>Plugins:AllowPrivateServers</c>).
/// </summary>
internal static partial class PluginServerUrl
{
    public const int MaxUrl = 2_048;

    /// <summary>Path segments this long, of letters and digits mixed, are read as a credential (Zapier- and Smithery-style URLs).</summary>
    public const int MinSecretSegment = 32;

    private static readonly string[] InternalSuffixes = [".localhost", ".local", ".internal", ".svc", ".cluster.local", ".home.arpa"];

    private static readonly HashSet<string> CredentialWords =
    [
        "key", "apikey", "token", "accesstoken", "secret", "auth", "authorization", "sig", "signature", "password", "passwd", "pwd", "pass",
        "code", "session", "sessionid", "credential", "credentials", "jwt", "bearer"
    ];

    /// <summary>A lowercase compound ending in one of these (<c>authtoken</c>, <c>clientsecret</c>) is a credential key too.</summary>
    private static readonly string[] CredentialSuffixes = ["key", "token", "secret", "password", "passwd", "signature", "credential", "credentials"];

    /// <returns>Why <paramref name="url"/> cannot be a plugin server's address, in words for the person; null when it can.</returns>
    public static string? Problem(string url, bool allowHttp, bool allowPrivate)
    {
        if (url.Length > MaxUrl || url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || url.Contains("${", StringComparison.Ordinal))
            return $"at most {MaxUrl} characters, no spaces, no ${{variables}} (a server's address is fixed in the plugin).";
        // .NET leaves a full-width or other non-ASCII host unmapped while kagent's Go client maps it (UTS-46), so the
        // checks below would judge another host than the one connected to.
        if (RawHost(url).Any(c => !char.IsAscii(c)))
            return "the host must be plain ASCII (an international name in its xn-- form).";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0
            || !(uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp)))
            return allowHttp ? "an absolute http(s) URL." : "an absolute https URL (Plugins:AllowHttp is off).";
        // .NET collapses dot segments, but kagent sends the path as written: the address checked must be the one asked for.
        if (RawPath(url).Split('/').Select(Uri.UnescapeDataString).Any(s => s is "." or ".."))
            return "its path has a '.' or '..' segment (also percent-encoded); write the path the server is at.";
        if (uri.UserInfo.Length > 0 || url.Contains('#') || CarriesCredential(uri) || SecretScan.Find(url) is not null)
            return "it carries a credential (user info, a key or token in the query, a fragment, or a long random path segment), which would go to git; "
                + "put it in a header instead, whose value Skanyxx keeps as a secret.";
        if (!allowPrivate && IsInternal(uri))
            return $"'{uri.Host}' is an internal address (loopback, private, link-local, a single-label or cluster name); kagent would reach it "
                + "from inside the cluster (Plugins:AllowPrivateServers is off).";
        return null;
    }

    /// <summary>
    /// Scheme, host, port and path (the URL without its query): the stored header values are sent only where they were
    /// entered for (D220). The path counts because multi-tenant hosts tell tenants apart by it. <see cref="Uri"/> already
    /// lowercases the scheme and host; the path keeps its case.
    /// </summary>
    public static string Address(string url) => new Uri(url).GetLeftPart(UriPartial.Path);

    /// <summary>
    /// The query key read as whole words (split on <c>_ - .</c> and camelCase), so <c>author</c> is not <c>auth</c>; a word
    /// that ends in a credential word (<c>accesskey</c>) counts too.
    /// </summary>
    public static bool IsCredentialKey(string key) =>
        Word().Matches(key).Select(w => w.Value.ToLowerInvariant()).Append(key.ToLowerInvariant())
            .Any(w => CredentialWords.Contains(w) || CredentialSuffixes.Any(s => w.EndsWith(s, StringComparison.Ordinal)));

    private static bool CarriesCredential(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
            .Any(p => IsCredentialKey(Uri.UnescapeDataString(p[0])) || (p.Length == 2 && IsRandom(Uri.UnescapeDataString(p[1]))))
        || uri.AbsolutePath.Split('/').Select(Uri.UnescapeDataString).Any(IsRandom);

    /// <summary>The host as written, before <see cref="Uri"/> maps it.</summary>
    private static string RawHost(string url)
    {
        var rest = url[(url.IndexOf("://", StringComparison.Ordinal) is var at and >= 0 ? at + 3 : 0)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>The path as written, before <see cref="Uri"/> normalises it.</summary>
    private static string RawPath(string url)
    {
        var rest = url[(url.IndexOf("://", StringComparison.Ordinal) is var at and >= 0 ? at + 3 : 0)..];
        var start = rest.IndexOf('/');
        if (start < 0)
            return "";
        var path = rest[start..];
        var end = path.IndexOfAny(['?', '#']);
        return end < 0 ? path : path[..end];
    }

    private static bool IsRandom(string segment) =>
        segment.Length >= MinSecretSegment && Token().IsMatch(segment) && segment.Any(char.IsDigit) && segment.Any(char.IsLetter) && Entropy(segment) >= 3.0;

    /// <summary>Shannon entropy in bits per character.</summary>
    private static double Entropy(string text) =>
        -text.GroupBy(c => c).Sum(g => (double)g.Count() / text.Length * Math.Log2((double)g.Count() / text.Length));

    /// <summary>
    /// An IP <see cref="NetworkGuard"/> does not call public, or a name that is not a public DNS name: localhost, a single
    /// label, a cluster or private suffix, or a last label that cannot be a top-level domain (letters only, at least two,
    /// or an IDN's <c>xn--</c> form).
    /// A trailing dot is dropped first, so <c>10.0.0.5.</c> is judged as the IP it resolves to.
    /// </summary>
    private static bool IsInternal(Uri uri)
    {
        var host = uri.DnsSafeHost.TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(host, out var address))
            return !NetworkGuard.IsPublic(address);
        return host == "localhost" || !host.Contains('.') || InternalSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal))
            || !TopLevel().IsMatch(host[(host.LastIndexOf('.') + 1)..]);
    }

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Word();

    [GeneratedRegex("^([a-z]{2,}|xn--[a-z0-9-]+)\\z")]
    private static partial Regex TopLevel();

    [GeneratedRegex("^[A-Za-z0-9_-]+\\z")]
    private static partial Regex Token();
}
