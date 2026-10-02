using System.Security.Claims;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>The group → role/team matrix on synthetic principals (D027, D1, D5), Graph faked.</summary>
public sealed class EntraMapperTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Oid = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string Admins = "00000000-0000-0000-0000-0000000000a1";
    private const string Billing = "00000000-0000-0000-0000-0000000000b1";
    private const string Builders = "00000000-0000-0000-0000-0000000000c1";
    private const string Unmapped = "00000000-0000-0000-0000-0000000000ff";

    private static readonly EntraConfig Config = new(true, Tenant, "22222222-2222-2222-2222-222222222222", "secret", new Dictionary<string, EntraGroupMapDto>
    {
        [Admins] = new(Admins, "Admins", [SkanyxxRoles.Supervisor], []),
        [Billing] = new(Billing, null, [SkanyxxRoles.Employee], ["billing"]),
        [Builders] = new(Builders, null, [SkanyxxRoles.Builder, SkanyxxRoles.Employee], ["platform"])
    }, 1);

    [Fact]
    public async Task MappedGroups_GiveTheUnionOfRolesAndTeams_UnmappedGroupsGiveNothing()
    {
        var mapping = await MapAsync(Principal(groups: [Billing, Builders, Unmapped]));

        Assert.True(mapping.GrantsAccess);
        Assert.Equal(2, mapping.MappedGroups);
        Assert.Equal([SkanyxxRoles.Builder, SkanyxxRoles.Employee], mapping.Roles);
        Assert.Equal(["billing", "platform"], mapping.Teams);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Unmapped)]
    [InlineData("Admins,not-a-guid")]
    public async Task NoMappedGroup_IsNoAccess(string groups)
    {
        var mapping = await MapAsync(Principal(groups.Split(',', StringSplitOptions.RemoveEmptyEntries)));

        Assert.False(mapping.GrantsAccess);
        Assert.Equal("no mapped group", mapping.RefusalReason);
        Assert.Empty(mapping.Roles);
    }

    [Fact]
    public async Task GroupIds_MatchWhateverTheirCase()
    {
        var mapping = await MapAsync(Principal(groups: [Admins.ToUpperInvariant()]));

        Assert.Equal([SkanyxxRoles.Supervisor], mapping.Roles);
    }

    [Fact]
    public async Task AGuest_IsRefused_EvenInAMappedGroup_AndGraphIsNotAsked()
    {
        var graph = new FakeGraph(Admins);

        var mapping = await MapAsync(Principal(groups: [Admins], overage: true, guest: true), graph);

        Assert.False(mapping.GrantsAccess);
        Assert.Equal("guest account", mapping.RefusalReason);
        Assert.Empty(graph.Calls);
    }

    [Fact]
    public async Task AMember_WithAcctZero_IsNotAGuest()
    {
        var mapping = await MapAsync(Principal(groups: [Admins], acct: "0"));

        Assert.True(mapping.GrantsAccess);
    }

    /// <summary>D9: without <c>acct</c> a guest cannot be told apart, so nobody gets in — and Graph is not asked.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    public async Task NoUsableAcct_IsUnknown_AndNoAccess(string? acct)
    {
        var graph = new FakeGraph(Admins);

        var mapping = await MapAsync(Principal(groups: [Admins], overage: true, acct: acct), graph);

        Assert.Equal(EntraAccountKind.Unknown, mapping.Kind);
        Assert.False(mapping.GrantsAccess);
        Assert.Equal("no acct claim in the id token", mapping.RefusalReason);
        Assert.Empty(graph.Calls);
    }

    /// <summary>
    /// D9: an <c>idp</c> that is not this tenant (another tenant, a personal account) is a guest even with <c>acct = 0</c>.
    /// SEC N5: only this tenant's exact v1/v2 issuer is; a value that merely contains the tenant id is a guest.
    /// </summary>
    [Theory]
    [InlineData("https://sts.windows.net/99999999-9999-9999-9999-999999999999/", false)]
    [InlineData("live.com", false)]
    [InlineData("https://sts.windows.net/11111111-1111-1111-1111-111111111111/", true)]
    [InlineData("HTTPS://STS.WINDOWS.NET/11111111-1111-1111-1111-111111111111/", true)]
    [InlineData("https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0", true)]
    [InlineData("https://sts.windows.net/11111111-1111-1111-1111-111111111111", false)]
    [InlineData("https://sts.windows.net/11111111-1111-1111-1111-111111111111/x", false)]
    [InlineData("https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0/", false)]
    [InlineData("https://partner.example/11111111-1111-1111-1111-111111111111/", false)]
    [InlineData("https://sts.windows.net.partner.example/11111111-1111-1111-1111-111111111111/", false)]
    [InlineData("11111111-1111-1111-1111-111111111111", false)]
    public async Task AnIdpClaim_ThatIsNotThisTenant_IsAGuest(string idp, bool member)
    {
        var mapping = await MapAsync(Principal(groups: [Admins], idp: idp));

        Assert.Equal(member ? EntraAccountKind.Member : EntraAccountKind.Guest, mapping.Kind);
        Assert.Equal(member, mapping.GrantsAccess);
    }

    [Theory]
    [InlineData("""{"groups":"src1"}""")]
    [InlineData("not json")]
    public async Task Overage_AsksGraphAboutTheMappedGroupsOnly_AndIgnoresTheTokensGroups(string claimNames)
    {
        var graph = new FakeGraph(Billing, Unmapped);

        var mapping = await MapAsync(Principal(groups: [Admins], claimNames: claimNames), graph);

        var call = Assert.Single(graph.Calls);
        Assert.Equal(Guid.Parse(Oid), call.ObjectId);
        Assert.Equal([Admins, Billing, Builders], call.Asked.Order());
        Assert.Equal([SkanyxxRoles.Employee], mapping.Roles);
        Assert.Equal(["billing"], mapping.Teams);
    }

    [Fact]
    public async Task HasGroups_IsAnOverageToo()
    {
        var graph = new FakeGraph(Admins);

        var mapping = await MapAsync(Principal(groups: [], hasGroups: true), graph);

        Assert.Single(graph.Calls);
        Assert.Equal([SkanyxxRoles.Supervisor], mapping.Roles);
    }

    [Fact]
    public async Task ClaimNamesForOtherClaims_IsNotAnOverage()
    {
        var graph = new FakeGraph();

        var mapping = await MapAsync(Principal(groups: [Admins], claimNames: """{"roles":"src2"}"""), graph);

        Assert.Empty(graph.Calls);
        Assert.Equal([SkanyxxRoles.Supervisor], mapping.Roles);
    }

    [Fact]
    public async Task Overage_WithNoMembershipInGraph_IsNoAccess()
    {
        var mapping = await MapAsync(Principal(groups: [], claimNames: """{"groups":"src1"}"""), new FakeGraph(Unmapped));

        Assert.False(mapping.GrantsAccess);
    }

    /// <summary>A stored rule naming owner (written around the validator) still never makes an owner.</summary>
    [Fact]
    public async Task TheOwnerRole_NeverComesOutOfTheMapping()
    {
        var config = Config with
        {
            Groups = new Dictionary<string, EntraGroupMapDto> { [Admins] = new(Admins, null, [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor], []) }
        };

        var mapping = await new EntraMapper(new FakeGraph()).MapAsync(Principal(groups: [Admins]), config, CancellationToken.None);

        Assert.Equal([SkanyxxRoles.Supervisor], mapping.Roles);
    }

    private static Task<EntraMapping> MapAsync(ClaimsPrincipal principal, FakeGraph? graph = null) =>
        new EntraMapper(graph ?? new FakeGraph()).MapAsync(principal, Config, CancellationToken.None);

    /// <param name="acct">Null leaves the optional claim out; <paramref name="guest"/> makes it <c>1</c>.</param>
    private static ClaimsPrincipal Principal(
        string[] groups, bool overage = false, bool guest = false, string? acct = "0", string? claimNames = null, bool hasGroups = false,
        string? idp = null)
    {
        var claims = new List<Claim> { new("tid", Tenant), new("oid", Oid) };
        claims.AddRange(groups.Select(g => new Claim("groups", g)));
        if (guest || acct is not null)
            claims.Add(new Claim("acct", guest ? "1" : acct!));
        if (idp is not null)
            claims.Add(new Claim("idp", idp));
        if (overage || claimNames is not null)
            claims.Add(new Claim("_claim_names", claimNames ?? """{"groups":"src1"}""", "JSON"));
        if (hasGroups)
            claims.Add(new Claim("hasgroups", "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
