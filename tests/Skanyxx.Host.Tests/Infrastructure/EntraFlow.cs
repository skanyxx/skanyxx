using System.Text.Json;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// A Microsoft sign-in as a browser does it, against <see cref="MockIdentityProvider"/>: follow the app's challenge to
/// the IdP, submit its login form with the id-token claims, then post the IdP's <c>form_post</c> answer (code + state)
/// back to <c>/signin-oidc</c> with the IdP's <c>Origin</c>, keeping the app's correlation and nonce cookies.
/// </summary>
public sealed partial class EntraFlow(MockIdentityProvider idp)
{
    /// <summary>The id-token claims Entra would send (the mock copies them into the token).</summary>
    /// <param name="acct">Null leaves the optional claim out, as an app registration without it does.</param>
    public static Dictionary<string, object> Claims(
        string tenant, string oid, string[]? groups = null, string email = "bea@contoso.example", string name = "Bea Entra", int? acct = 0)
    {
        var claims = new Dictionary<string, object> { ["tid"] = tenant, ["oid"] = oid, ["email"] = email, ["name"] = name };
        if (acct is not null)
            claims["acct"] = acct;
        if (groups is not null)
            claims["groups"] = groups;
        return claims;
    }

    /// <summary>The <c>state</c> of a challenge's authorize redirect, for answering it as the IdP would without signing in.</summary>
    public static string StateOf(HttpResponseMessage challenge) => System.Web.HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["state"]!;

    /// <summary>From the challenge (a 302 to the IdP) to the app's answer to the callback.</summary>
    public async Task<HttpResponseMessage> AtMicrosoftAsync(Browser browser, HttpResponseMessage challenge, IDictionary<string, object> claims)
    {
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.StartsWith(idp.BaseUrl + "/", authorize.ToString());
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var login = await http.PostAsync(authorize, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = claims["oid"].ToString()!,
            ["claims"] = JsonSerializer.Serialize(claims)
        }));
        var html = await login.Content.ReadAsStringAsync();
        Assert.True(login.StatusCode == HttpStatusCode.OK, html);
        var fields = HiddenField().Matches(html).ToDictionary(m => m.Groups[1].Value, m => WebUtility.HtmlDecode(m.Groups[2].Value));
        Assert.Equal(["code", "state"], fields.Keys.Order());
        return await browser.PostFormAsync(GuardedPaths.ExternalSignInCallback, fields, request => request.Headers.Add("Origin", idp.Origin));
    }

    /// <summary>"Sign in with Microsoft" on /Login through to the completion's answer (a redirect to the return URL, or the page with the refusal).</summary>
    public async Task<HttpResponseMessage> SignInAsync(Browser browser, IDictionary<string, object> claims, string returnUrl = "/")
    {
        var challenge = await browser.SubmitAsync($"/Login?handler=Microsoft&returnUrl={Uri.EscapeDataString(returnUrl)}", [], tokenFrom: "/Login");
        return await CompleteAsync(browser, await AtMicrosoftAsync(browser, challenge, claims));
    }

    /// <summary>Follows the callback's redirect to the page that finishes the sign-in or link.</summary>
    public static async Task<HttpResponseMessage> CompleteAsync(Browser browser, HttpResponseMessage callback)
    {
        Assert.True(callback.StatusCode == HttpStatusCode.Redirect, $"{(int)callback.StatusCode}: {await callback.Content.ReadAsStringAsync()}");
        return await browser.GetAsync(callback.Headers.Location!.OriginalString);
    }

    [GeneratedRegex("""<input type="hidden" name="([^"]+)" value="([^"]*)"\s*/?>""")]
    private static partial Regex HiddenField();
}
