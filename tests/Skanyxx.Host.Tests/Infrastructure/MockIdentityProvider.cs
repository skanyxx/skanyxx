using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// navikt/mock-oauth2-server in a container, standing in for Microsoft Entra ID (verified in A4: its interactive login
/// takes a JSON <c>claims</c> field that lands in the id token — oid, tid, groups, _claim_names, acct — it supports
/// PKCE S256 and <c>form_post</c>). Any path segment is an issuer, so each tenant GUID gets its own issuer at
/// <c>{BaseUrl}/{tenant}</c>, exactly like Entra's per-tenant authority.
/// </summary>
public sealed class MockIdentityProvider : IAsyncLifetime
{
    public const string Image = "ghcr.io/navikt/mock-oauth2-server:6.0.4";

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(8080, true)
        .WithEnvironment("JSON_CONFIG", """{"interactiveLogin":true}""")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/default/.well-known/openid-configuration").ForPort(8080)))
        .Build();

    /// <summary>What the app and the test "browser" both use, so the issuer in the discovery document matches the tokens.</summary>
    public string BaseUrl => $"http://localhost:{_container.GetMappedPublicPort(8080)}";

    public string Origin => BaseUrl;

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
