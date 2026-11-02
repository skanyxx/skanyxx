using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Host.Tests;

/// <summary>D557: the one rule for which kagent agents Chat, Investigate and the Agents page offer.</summary>
public sealed class ChatOfferTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("skanyxx.dev/merged", "true", true)]
    [InlineData("skanyxx.dev/managed-by", "skanyxx-agents", true)]
    [InlineData("app.kubernetes.io/part-of", "skanyxx-memory", true)]
    [InlineData("app.kubernetes.io/part-of", "billing", true)]
    [InlineData("skanyxx.dev/preview", "12", false)]
    [InlineData("skanyxx.dev/role", "factory", false)]
    [InlineData("app.kubernetes.io/part-of", "skanyxx-ticket-flow", false)]
    [InlineData("app.kubernetes.io/part-of", "skanyxx-something-new", false)]
    public void ALabel_DecidesWhetherChatOffersIt(string? label, string? value, bool offered)
    {
        var agent = new Agent { Name = "a", Labels = label is null ? [] : new() { [label] = value! } };

        Assert.Equal(offered, ChatOffer.IsChatAgent(agent));
        Assert.Equal(offered, ChatOffer.Offers(agent));
    }

    [Theory]
    [InlineData("skanyxx-factory")]
    [InlineData("skanyxx-anything")]
    [InlineData("preview-4-helper")]
    public void AReservedName_IsNeverOffered_EvenUnlabelled(string name) =>
        Assert.False(ChatOffer.Offers(new Agent { Name = name }));

    [Fact]
    public void ASuspendedAgent_IsNeverOffered_ThoughItIsAChatAgent()
    {
        var agent = new Agent { Name = "a", Suspended = true };

        Assert.True(ChatOffer.IsChatAgent(agent));
        Assert.False(ChatOffer.Offers(agent));
    }

    /// <summary>Its own memory server is fine; another agent's would act in memory as that agent, possibly for the person.</summary>
    [Theory]
    [InlineData("kagent/skanyxx-memory-seed", true)]
    [InlineData("kagent/docs-mcp", true)]
    [InlineData("kagent/skanyxx-memory-refund-helper", false)]
    [InlineData("other/skanyxx-memory-seed", false)]
    [InlineData("kagent/skanyxx-memory-preview-3-seed", false)]
    public void AMemoryServer_IsOnlyItsOwn(string server, bool offered) =>
        Assert.Equal(offered, ChatOffer.Offers(new Agent { Namespace = "kagent", Name = "seed", McpServerRefs = [server] }));
}
