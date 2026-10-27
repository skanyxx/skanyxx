using System.Security.Claims;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.ChatContext.Tests;

/// <summary>D237: a chat reader is a signed-in person, from the authenticated principal only.</summary>
public sealed class ChatReaderTests
{
    private static ClaimsPrincipal Principal(string? userId, string? authenticationType = "Test", params Claim[] extra) =>
        new(new ClaimsIdentity([.. userId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, .. extra], authenticationType));

    [Fact]
    public void APerson_IsReadWithTheirRoles()
    {
        var reader = ChatReader.From(Principal("ana", extra: new Claim(ClaimTypes.Role, SkanyxxRoles.Supervisor)));

        Assert.Equal("ana", reader.UserId);
        Assert.True(reader.IsSupervisor);
        Assert.False(reader.IsOwner);
    }

    [Fact]
    public void AnAgentOrAnAnonymousPrincipal_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => ChatReader.From(Principal("seed", AgentPrincipal.SchemeName)));
        Assert.Throws<InvalidOperationException>(() => ChatReader.From(Principal("seed", extra: new Claim(AgentPrincipal.ActsForUsersClaim, "true"))));
        Assert.Throws<InvalidOperationException>(() => ChatReader.From(Principal("ana", authenticationType: null)));
    }
}
