using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Agents.Plugins.Sources;

/// <summary>
/// A plugin source from a git repository pinned to a full commit SHA (P9), downloaded as the host's zip archive of that
/// commit: github.com through codeload, other hosts only when the owner listed them (<see cref="PluginsOptions.GitHosts"/>).
/// The download is bounded in time and size, follows at most <see cref="MaxRedirects"/> redirects on the same host, and
/// connects only to public addresses unless <see cref="PluginsOptions.AllowPrivateNetworks"/>.
/// </summary>
internal sealed partial class GitArchiveSource(IHttpClientFactory clients, IOptions<PluginsOptions> options)
{
    public const string HttpClientName = "skanyxx-plugins-git";

    public const int MaxRedirects = 3;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The files of <paramref name="repoUrl"/> at <paramref name="sha"/>. The digest is the tree's
    /// (<see cref="PluginTree.Digest"/>), not the archive's: a host may re-encode an archive of the same commit.
    /// </summary>
    public async Task<PluginArchive> FetchAsync(string repoUrl, string sha, CancellationToken ct)
    {
        var settings = options.Value;
        var url = ArchiveUrl(repoUrl, sha, settings);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        var client = clients.CreateClient(HttpClientName);
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var next = new Uri(url, location);
                    if (redirects == MaxRedirects)
                        throw new PluginSourceException($"The git host redirected more than {MaxRedirects} times.", upstream: true);
                    if (!string.Equals(next.Authority, url.Authority, StringComparison.OrdinalIgnoreCase) || next.Scheme != url.Scheme)
                        throw new PluginSourceException($"The git host redirected to {next.Scheme}://{next.Authority}; only redirects on the same host are followed.", upstream: true);
                    url = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new PluginSourceException($"The git host answered {(int)response.StatusCode} for the archive of {sha}.", upstream: true);
                if (response.Content.Headers.ContentLength > settings.MaxDownloadBytes)
                    throw new PluginSourceException($"The archive is larger than {settings.MaxDownloadBytes / (1024 * 1024)} MB.");
                await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
                var archive = await ZipPluginSource.ReadAsync(body, settings.MaxDownloadBytes, deadline.Token);
                return archive with { Digest = archive.Tree.Digest() };
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PluginSourceException($"The download did not finish within {Timeout.TotalSeconds:0} seconds.", upstream: true);
        }
        catch (HttpRequestException ex)
        {
            throw ex.InnerException as PluginSourceException ?? new PluginSourceException($"The git host could not be reached: {ex.Message}", upstream: true);
        }
    }

    /// <summary>The zip archive URL of <paramref name="repoUrl"/> (<c>https://host/owner/repo[.git]</c>) at <paramref name="sha"/>.</summary>
    public static Uri ArchiveUrl(string repoUrl, string sha, PluginsOptions settings)
    {
        if (!Sha().IsMatch(sha))
            throw new PluginSourceException("The commit must be a full 40-character lowercase SHA.");
        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var repo)
            || !(repo.Scheme == Uri.UriSchemeHttps || (settings.AllowHttp && repo.Scheme == Uri.UriSchemeHttp))
            || repo.UserInfo.Length > 0 || repo.Query.Length > 0 || repo.Fragment.Length > 0)
            throw new PluginSourceException($"The repository must be {(settings.AllowHttp ? "an http(s)" : "an https")} URL like https://host/owner/repo, without credentials or a query.");
        var segments = repo.AbsolutePath.Trim('/').Split('/');
        segments[^1] = segments[^1].EndsWith(".git", StringComparison.Ordinal) ? segments[^1][..^4] : segments[^1];
        if (segments.Length < 2 || segments.Any(s => !PathSegment().IsMatch(s) || s is "." or ".."))
            throw new PluginSourceException("The repository URL must name an owner and a repository: https://host/owner/repo.");
        var (owner, name) = (string.Join('/', segments[..^1]), segments[^1]);

        if (repo.Host is "github.com" or "www.github.com" && repo.Scheme == Uri.UriSchemeHttps && repo.IsDefaultPort && segments.Length == 2)
            return new Uri($"https://codeload.github.com/{owner}/{name}/zip/{sha}");
        var host = settings.GitHosts.FirstOrDefault(h => string.Equals(h.Host, repo.Authority, StringComparison.OrdinalIgnoreCase))
            ?? throw new PluginSourceException($"{repo.Authority} is not github.com and not a git host the owner allowed (Plugins:GitHosts).");
        var root = $"{repo.Scheme}://{repo.Authority}/{owner}/{name}";
        return host.Kind.ToLowerInvariant() switch
        {
            "gitea" when segments.Length == 2 => new Uri($"{root}/archive/{sha}.zip"),
            "gitlab" => new Uri($"{root}/-/archive/{sha}/{name}-{sha}.zip"),
            "gitea" => throw new PluginSourceException("A Gitea repository URL is https://host/owner/repo."),
            _ => throw new PluginSourceException($"Git host {host.Host} has kind '{host.Kind}'; it must be gitea or gitlab.")
        };
    }

    /// <summary>
    /// The handler behind <see cref="HttpClientName"/>: no automatic redirects (each is checked), no proxy (it would
    /// connect on our behalf, past the address check), and every connection goes to an address checked here.
    /// </summary>
    public static SocketsHttpHandler Handler(PluginsOptions settings) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = (context, ct) => ConnectAsync(context.DnsEndPoint, settings.AllowPrivateNetworks, ct)
    };

    /// <summary>Resolves once and connects to the addresses it checked, so a second DNS answer cannot swap them (rebinding).</summary>
    internal static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, bool allowPrivate, CancellationToken ct)
    {
        IPAddress[] addresses = IPAddress.TryParse(endPoint.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(endPoint.Host, ct);
        if (!allowPrivate && addresses.FirstOrDefault(a => !NetworkGuard.IsPublic(a)) is { } blocked)
            throw new PluginSourceException($"{endPoint.Host} resolves to {blocked}, a loopback, private or link-local address; refused (Plugins:AllowPrivateNetworks).");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    [GeneratedRegex("^[0-9a-f]{40}\\z")]
    private static partial Regex Sha();

    [GeneratedRegex("^[A-Za-z0-9_.-]+\\z")]
    private static partial Regex PathSegment();
}
