using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// Just enough of a browser for the Razor forms: keeps cookies by name (sent over plain http even when Secure — the
/// test host has no TLS) and submits a form with the antiforgery token read from the page.
/// </summary>
public sealed partial class Browser(HostApp host)
{
    private readonly Dictionary<string, string> _cookies = [];

    public bool HasCookie(string name) => _cookies.ContainsKey(name);

    public async Task<HttpResponseMessage> GetAsync(string path) => await SendAsync(new HttpRequestMessage(HttpMethod.Get, path));

    /// <summary>GETs <paramref name="page"/>, then posts <paramref name="fields"/> with the form's antiforgery token.</summary>
    public async Task<HttpResponseMessage> SubmitAsync(string page, Dictionary<string, string> fields, string? tokenFrom = null)
    {
        var html = await (await GetAsync(tokenFrom ?? page)).Content.ReadAsStringAsync();
        var token = AntiforgeryField().Match(html).Groups[1].Value;
        return await PostFormAsync(page, new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token });
    }

    public async Task<HttpResponseMessage> PostFormAsync(string page, Dictionary<string, string> fields, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, page) { Content = new FormUrlEncodedContent(fields) };
        configure?.Invoke(request);
        return await SendAsync(request);
    }

    /// <summary>Sends with the cookies kept so far (and remembers any it gets back).</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        if (_cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        var response = await host.Client().SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            foreach (var cookie in SetCookieHeaderValue.ParseList(setCookies.ToList()))
                if (cookie.Expires < DateTimeOffset.UtcNow)
                    _cookies.Remove(cookie.Name.ToString());
                else
                    _cookies[cookie.Name.ToString()] = cookie.Value.ToString();
        return response;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}
