using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Tests;

public sealed class BootstrapTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    [Fact]
    public async Task Status_FlipsWhenTheOwnerIsCreated()
    {
        Assert.False(await BootstrappedAsync());

        var response = await App.BootstrapAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(await BootstrappedAsync());
    }

    [Fact]
    public async Task Bootstrap_CreatesAnOwner_WithALowercaseGuidId()
    {
        var response = await App.BootstrapAsync();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var id = body.RootElement.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(id.ToLowerInvariant(), id);
        Assert.True(Identifier.IsValid(id));
        Assert.Equal(IdentityApp.OwnerEmail, body.RootElement.GetProperty("email").GetString());
        Assert.Equal("The Owner", body.RootElement.GetProperty("displayName").GetString());
        Assert.Equal([SkanyxxRoles.Owner], body.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task SecondBootstrap_Is409_AndCreatesNobody()
    {
        await App.BootstrapAsync();

        var second = await App.BootstrapAsync("intruder@evil.example", "another long password");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await Postgres.UserCountAsync());
        // A problem, not ConflictResponse: there is no current state to hand back (verifier G8).
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("\"current\"", await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ParallelBootstraps_ExactlyOneWins()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => App.BootstrapAsync($"owner{i}@skanyxx.example", "correct horse battery")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await Postgres.UserCountAsync());
    }

    [Fact]
    public async Task ShortPassword_Is400_KeyedByPassword()
    {
        var response = await App.BootstrapAsync(password: "elevenchars");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Password", out _));
        Assert.Equal(0, await Postgres.UserCountAsync());
    }

    [Fact]
    public async Task ForwardedRequest_WithoutToken_Is403()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/bootstrap")
        {
            Content = JsonContent.Create(new { email = IdentityApp.OwnerEmail, password = IdentityApp.OwnerPassword })
        };
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        var response = await App.Client().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await Postgres.UserCountAsync());
    }

    [Fact]
    public async Task NonLoopback_WithoutToken_IsForbidden()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var outcome = await mediator.Send(new BootstrapOwnerCommand(IdentityApp.OwnerEmail, IdentityApp.OwnerPassword, null, null, FromLoopback: false), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeStatus.Forbidden, outcome.Status);
        Assert.Equal(0, await Postgres.UserCountAsync());
    }

    private async Task<bool> BootstrappedAsync()
    {
        var status = await App.Client().GetFromJsonAsync<JsonElement>("/api/identity/status");
        return status.GetProperty("bootstrapped").GetBoolean();
    }
}
