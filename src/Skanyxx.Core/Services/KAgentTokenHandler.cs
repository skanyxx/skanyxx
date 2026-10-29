using System.Net.Http.Headers;

namespace Skanyxx.Core.Services;

/// <summary>
/// D521: every request bound for kagent's origin leaves with a fresh <see cref="KAgentToken"/> naming its user, and
/// without <c>?user_id=</c> or <c>X-User-Id</c> (the gate refuses the first and replaces the second). The user is the one
/// the caller named, as kagent would read it: <c>?user_id=</c>, else <c>X-User-Id</c>, else <see cref="ServiceUser"/>.
/// It is on every HttpClient (Investigate and Tickets build their own for kagent), so any other origin passes untouched.
/// </summary>
public sealed class KAgentTokenHandler(KAgentToken token, Uri kagent) : DelegatingHandler
{
    /// <summary>The user of a call that names none (the health ping).</summary>
    public const string ServiceUser = "skanyxx";

    private const string UserIdKey = "user_id";
    private const string UserIdHeader = "X-User-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && Uri.Compare(uri, kagent, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            var pairs = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var named = pairs.Select(p => p.Split('=', 2)).Where(kv => kv[0] == UserIdKey)
                .Select(kv => Uri.UnescapeDataString(kv.ElementAtOrDefault(1) ?? "")).FirstOrDefault(u => u.Length > 0);
            var user = named ?? (request.Headers.TryGetValues(UserIdHeader, out var values) ? values.First() : ServiceUser);
            request.RequestUri = new UriBuilder(uri) { Query = string.Join('&', pairs.Where(p => p.Split('=', 2)[0] != UserIdKey)) }.Uri;
            request.Headers.Remove(UserIdHeader);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Issue(user));
        }
        return base.SendAsync(request, cancellationToken);
    }
}
