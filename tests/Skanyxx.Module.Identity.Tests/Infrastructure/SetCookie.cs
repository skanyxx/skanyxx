using Microsoft.Net.Http.Headers;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

public static class SetCookie
{
    public const string AuthCookie = "skanyxx.auth";

    public static SetCookieHeaderValue Auth(HttpResponseMessage response) =>
        SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList())
            .Single(c => c.Name == AuthCookie);

    /// <summary>The <c>Cookie</c> header a browser would send back.</summary>
    public static string AuthHeader(HttpResponseMessage response) => $"{AuthCookie}={Auth(response).Value}";
}
