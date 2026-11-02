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
    [InlineData("skanyxx.dev/preview", "12", false)]
    [InlineData("skanyxx.dev/role", "factory", false)]
    [InlineData("app.kubernetes.io/part-of", "skanyxx-ticket-flow", false)]
    public void ALabel_DecidesWhetherChatOffersIt(string? label, string? value, bool offered)
    {
        var agent = new Agent { Name = "a", Labels = label is null ? [] : new() { [label] = value! } };

        Assert.Equal(offered, ChatOffer.IsChatAgent(agent.Labels));
        Assert.Equal(offered, ChatOffer.Offers(agent));
    }

    [Fact]
    public void ASuspendedAgent_IsNeverOffered_ThoughItIsAChatAgent()
    {
        var agent = new Agent { Name = "a", Suspended = true };

        Assert.True(ChatOffer.IsChatAgent(agent.Labels));
        Assert.False(ChatOffer.Offers(agent));
    }
}
