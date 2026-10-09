using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>The owner lists people, changes their roles and disables/enables them; the owner account is off limits.</summary>
public sealed class PeopleTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    private string _owner = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
        _owner = (await App.SignInBearerAsync()).AccessToken;
    }

    [Fact]
    public async Task List_ShowsEveryoneWithRolesAndStatus()
    {
        var (_, memberId) = await App.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder, SkanyxxRoles.Employee);

        var people = await App.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/people", cancellationToken: TestContext.Current.CancellationToken);
        var member = people.EnumerateArray().Single(p => p.GetProperty("id").GetString() == memberId);
        var owner = people.EnumerateArray().Single(p => p.GetProperty("id").GetString() != memberId);

        Assert.Equal(2, people.GetArrayLength());
        Assert.Equal(["builder", "employee"], member.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal("Bea Builder", member.GetProperty("displayName").GetString());
        Assert.False(member.GetProperty("disabled").GetBoolean());
        Assert.Equal(["owner"], owner.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task SetRoles_ReplacesTheRoles_AndANewSignInCarriesThem()
    {
        var (_, memberId) = await App.AddMemberAsync(_owner);

        var set = await SetRolesAsync(memberId, SkanyxxRoles.Supervisor, SkanyxxRoles.Employee);
        var fresh = await App.SignInBearerAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword);
        var probe = await App.Client(bearer: fresh.AccessToken).GetFromJsonAsync<JsonElement>(IdentityApp.ProbePath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(["employee", "supervisor"], probe.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.True(probe.GetProperty("supervisor").GetBoolean());
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("root")]
    public async Task SetRoles_OutsideTheGrantableSet_Is400(string role)
    {
        var (_, memberId) = await App.AddMemberAsync(_owner);

        var response = await SetRolesAsync(memberId, role);
        var people = await App.Client(bearer: _owner).GetStringAsync("/api/identity/people", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(people, "\"owner\""));
    }

    /// <summary>CR3: the owner's roles are fixed; saving them would only sign the owner out everywhere.</summary>
    [Fact]
    public async Task TheOwnersRoles_AreFixed_403()
    {
        var ownerId = (await Postgres.OwnerAsync()).Id;

        var none = await SetRolesAsync(ownerId);
        var hats = await SetRolesAsync(ownerId, SkanyxxRoles.Builder);
        var stillSignedIn = await App.Client(bearer: _owner).GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken);
        var people = await App.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/people", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, hats.StatusCode);
        Assert.Contains("The owner's roles are fixed", await hats.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
        Assert.Equal(["owner"], people[0].GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    /// <summary>CR5: a duplicate display-name claim is not a 500; the first one is shown.</summary>
    [Fact]
    public async Task List_WithADuplicateDisplayNameClaim_ShowsTheFirst()
    {
        var (_, memberId) = await App.AddMemberAsync(_owner);
        await using (var db = Postgres.CreateDbContext())
        {
            db.UserClaims.Add(new IdentityUserClaim<string> { UserId = memberId, ClaimType = SkanyxxClaims.DisplayName, ClaimValue = "Second Name" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await App.Client(bearer: _owner).GetAsync("/api/identity/people", TestContext.Current.CancellationToken);
        var member = (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).EnumerateArray().Single(p => p.GetProperty("id").GetString() == memberId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bea Builder", member.GetProperty("displayName").GetString());
    }

    /// <summary>
    /// CR2: the person may be tracked by the request before the account lock (the caller's own authentication loads
    /// them); a failed sign-in committed in between must not turn the change into a concurrency failure.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Change_OnAPersonTrackedBeforeTheLock_UsesTheRowAsItIsNow(bool disable)
    {
        var ownerId = (await Postgres.OwnerAsync()).Id;
        var (_, memberId) = await App.AddMemberAsync(_owner);
        await using var scope = App.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.NotNull(await users.FindByIdAsync(memberId)); // tracked in this scope, stamp as of now

        await using (var other = App.Services.CreateAsyncScope())
        {
            var otherUsers = other.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await otherUsers.AccessFailedAsync((await otherUsers.FindByIdAsync(memberId))!)).Succeeded);
        }

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var outcome = disable
            ? await mediator.Send(new SetDisabledCommand(ownerId, memberId, Disabled: true), TestContext.Current.CancellationToken)
            : await mediator.Send(new SetRolesCommand(ownerId, memberId, [SkanyxxRoles.Employee]), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.Ok, outcome.Status);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("enable")]
    public async Task TheOwner_CannotBeDisabledOrEnabled(string action)
    {
        var ownerId = (await Postgres.OwnerAsync()).Id;

        var response = await App.Client(bearer: _owner).PostAsync($"/api/identity/people/{ownerId}/{action}", null, TestContext.Current.CancellationToken);
        var signIn = await App.SignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task Disable_RefusesSignIn_WithTheGenericAnswer_AndEnableRestoresIt()
    {
        var (_, memberId) = await App.AddMemberAsync(_owner);

        var disable = await App.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        var refused = await App.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword);
        var enable = await App.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/enable", null, TestContext.Current.CancellationToken);
        var allowed = await App.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword);

        Assert.True((await disable.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("disabled").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("Invalid email or password.", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False((await enable.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("disabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task UnknownPerson_Is404()
    {
        var roles = await SetRolesAsync(Guid.NewGuid().ToString(), SkanyxxRoles.Builder);
        var disable = await App.Client(bearer: _owner).PostAsync($"/api/identity/people/{Guid.NewGuid()}/disable", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, roles.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, disable.StatusCode);
    }

    private Task<HttpResponseMessage> SetRolesAsync(string userId, params string[] roles) =>
        App.Client(bearer: _owner).PutAsJsonAsync($"/api/identity/people/{userId}/roles", new { roles });
}
