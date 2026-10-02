using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// SEC M2: when identity announces that a person lost supervisor or was disabled, every agent secret they issued stops
/// working on <c>/mcp/memory</c>; secrets issued by anyone else, the owner's acts-for-users ones included, keep working.
/// </summary>
public sealed class OffboardingTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    [Theory]
    [InlineData("supervisor role removed")]
    [InlineData("account disabled")]
    public async Task PrivilegesRevoked_RevokesTheSecretsThePersonIssued_Only(string reason)
    {
        var first = await App.IssueSecretAsync("boss-agent-1");
        var second = await App.IssueSecretAsync("boss-agent-2");
        var owners = await App.IssueSecretAsync("owner-agent", actsForUsers: true);

        await PublishAsync(new PrivilegesRevoked(MemoryApp.Supervisor, reason));

        Assert.Equal(HttpStatusCode.Unauthorized, (await App.PostMcpAsync("tools/list", $"Bearer {first}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.PostMcpAsync("tools/list", $"Bearer {second}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await App.PostMcpAsync("tools/list", $"Bearer {owners}")).StatusCode);
        Assert.Contains($"Agent memory secret for boss-agent-1 revoked: its issuer {MemoryApp.Supervisor} lost privileges ({reason})", App.Logs);
        Assert.Contains($"Agent memory secret for boss-agent-2 revoked", App.Logs);
        Assert.DoesNotContain("owner-agent revoked", App.Logs);
        Assert.DoesNotContain(first, App.Logs);
        Assert.DoesNotContain(second, App.Logs);
    }

    [Fact]
    public async Task PrivilegesRevoked_ForSomeoneWhoIssuedNothing_ChangesNothing()
    {
        var secret = await App.IssueSecretAsync("boss-agent");

        await PublishAsync(new PrivilegesRevoked("someone-else", "account disabled"));

        Assert.Equal(HttpStatusCode.OK, (await App.PostMcpAsync("tools/list", $"Bearer {secret}")).StatusCode);
        Assert.DoesNotContain("lost privileges", App.Logs);
    }

    private async Task PublishAsync(PrivilegesRevoked notification)
    {
        await using var scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(notification);
    }
}
