using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Which POSTs spend the sign-in window: one Microsoft sign-in counts once, at its callback (SEC L2); on the Account
/// page only the link, which checks the password.
/// </summary>
public sealed class GuardedPathsTests
{
    [Theory]
    [InlineData("POST", "/Login", "", true)]
    [InlineData("POST", "/Login", "?handler=Microsoft", false)]
    [InlineData("POST", "/Login", "?handler=microsoft&returnUrl=%2F", false)]
    [InlineData("POST", "/signin-oidc", "", true)]
    [InlineData("POST", "/Account", "?handler=LinkMicrosoft", true)]
    [InlineData("POST", "/Account", "?handler=linkmicrosoft", true)]
    [InlineData("POST", "/Account", "?handler=Other&handler=LinkMicrosoft", true)]
    [InlineData("POST", "/Account", "", false)]
    [InlineData("POST", "/Account", "?handler=Other", false)]
    [InlineData("GET", "/Account", "?handler=LinkMicrosoft", false)]
    [InlineData("POST", "/api/identity/sign-in", "", true)]
    [InlineData("GET", "/Login", "?handler=Microsoft", false)]
    public void CredentialPosts(string method, string path, string query, bool counted)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        Assert.Equal(counted, GuardedPaths.IsCredentialPost(context.Request));
    }

    [Theory]
    [InlineData("/api/toolservers", true)]
    [InlineData("/api/agents/kagent/a", true)]
    [InlineData("/api/analytics/usage", true)]
    [InlineData("/api/dashboard/health", true)]
    [InlineData("/api/me/preferences", true)]
    [InlineData("/api/setup/guides/models", true)]
    [InlineData("/api/memory/cards", true)]
    [InlineData("/api/investigate/runs", true)]
    [InlineData("/api/meta", false)]
    [InlineData("/health", false)]
    public void RateLimitedAndOriginGuarded(string path, bool guarded) => Assert.Equal(guarded, GuardedPaths.Contains(path));
}
