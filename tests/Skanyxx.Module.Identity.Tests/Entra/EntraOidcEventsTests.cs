using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>The handler hooks: tenant pin, the <c>tid|oid</c> account key (never <c>sub</c> or email), the registered redirect URI.</summary>
public sealed class EntraOidcEventsTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Oid = "aaaaaaaa-0000-0000-0000-000000000001";

    [Fact]
    public async Task ATokenFromTheTenant_IsKeyedByTidAndOid_ReplacingAnyNameIdentifierItCarried()
    {
        var context = Validated(("tid", Tenant.ToUpperInvariant()), ("oid", Oid), ("sub", "pairwise-sub"),
            (ClaimTypes.NameIdentifier, "spoofed"), ("email", "bea@contoso.example"));

        await Events().TokenValidated(context);

        Assert.Null(context.Result);
        Assert.Equal($"{Tenant}|{Oid}", Assert.Single(context.Principal!.FindAll(ClaimTypes.NameIdentifier)).Value);
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222", Oid)]
    [InlineData(null, Oid)]
    [InlineData("not-a-guid", Oid)]
    [InlineData(Tenant, null)]
    [InlineData(Tenant, "not-a-guid")]
    public async Task AnotherTenant_OrNoObjectId_FailsTheCallback(string? tid, string? oid)
    {
        var claims = new List<(string, string)>();
        if (tid is not null)
            claims.Add(("tid", tid));
        if (oid is not null)
            claims.Add(("oid", oid));
        var context = Validated([.. claims]);

        await Events().TokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        Assert.DoesNotContain(context.Principal!.Claims, c => c.Type == ClaimTypes.NameIdentifier);
    }

    [Fact]
    public async Task TheRedirectUri_IsBuiltOnPublicBaseUrl_NotOnTheRequest()
    {
        var context = new RedirectContext(new DefaultHttpContext(), Scheme, new OpenIdConnectOptions(), new AuthenticationProperties())
        {
            ProtocolMessage = new OpenIdConnectMessage { RedirectUri = "http://internal:8080/signin-oidc" }
        };

        await Events("https://skanyxx.example/").RedirectToIdentityProvider(context);

        Assert.Equal("https://skanyxx.example/signin-oidc", context.ProtocolMessage.RedirectUri);
    }

    private static readonly AuthenticationScheme Scheme = new(EntraScheme.Name, null, typeof(OpenIdConnectHandler));

    private static EntraOidcEvents Events(string? publicBaseUrl = null) => new(Tenant, new EntraRedirectUri(
        Options.Create(new IdentityModuleOptions { PublicBaseUrl = publicBaseUrl }), new HostingEnvironment { EnvironmentName = "Production" }));

    private static TokenValidatedContext Validated(params (string Type, string Value)[] claims) =>
        new(new DefaultHttpContext(), Scheme, new OpenIdConnectOptions(),
            new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "oidc")), new AuthenticationProperties());
}
