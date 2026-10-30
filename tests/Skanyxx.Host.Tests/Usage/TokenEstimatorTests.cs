using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Host.Tests.Usage;

/// <summary>D548: one estimate of what Skanyxx sends, apart from the model's own count.</summary>
public sealed class TokenEstimatorTests
{
    private static readonly ITokenEstimator Estimator = CharTokenEstimator.Instance;

    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    public void Text_IsFourCharactersAToken_RoundedUp(string text, long tokens) =>
        Assert.Equal(tokens, Estimator.Count(text, "kagent/any"));

    [Fact]
    public void Bytes_AreCountedAsTheBase64OnTheWire()
    {
        var bytes = new byte[1000];
        Assert.Equal(Estimator.Count(Convert.ToBase64String(bytes)), Estimator.CountBase64(bytes.Length));
        Assert.Equal(0, Estimator.CountBase64(0));
    }

    [Fact]
    public void Measure_KeepsTheClientCount_ApartFromTheModelsCount()
    {
        var prompt = new string('p', 400);
        var reported = ChatQuotaUsage.Measure(Estimator, "ann", "kagent/a", "kagent/m", "c", new ChatUsage(1000, 50, 1050), prompt, "answer");
        Assert.False(reported.Estimated);
        Assert.Equal(1050, reported.Usage.Total);
        Assert.Equal(100, reported.ClientTokens);

        // kagent reported nothing: the model's tokens are the estimate (prompt and answer), still kept apart.
        var estimated = ChatQuotaUsage.Measure(Estimator, "ann", "kagent/a", "kagent/m", "c", ChatUsage.None, prompt, new string('a', 40));
        Assert.True(estimated.Estimated);
        Assert.Equal(new ChatUsage(100, 10, 110), estimated.Usage);
        Assert.Equal(100, estimated.ClientTokens);

        // Chat counted its images too: the estimate's input is everything sent, and so is the client count.
        var withImages = ChatQuotaUsage.Measure(Estimator, "ann", "kagent/a", "kagent/m", "c", ChatUsage.None, prompt, "", sent: 900);
        Assert.Equal(900, withImages.Usage.Prompt);
        Assert.Equal(900, withImages.ClientTokens);
    }

    [Theory]
    [InlineData("US$")]
    [InlineData("usd")]
    [InlineData("")]
    public void ACurrencyThatIsNotAnIsoCode_IsRefusedAtStart(string currency)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Analytics:Currency"] = currency }).Build();
        using var services = new ServiceCollection().AddTokenUsage(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<CostCalculator>());
    }

    [Fact]
    public void TheCalculator_PricesInputAndOutputApart_AndNamesUnpricedModels()
    {
        var cost = new CostCalculator(new PricingOptions { Prices = { ["kagent/m"] = new ModelPrice { InputPerMillion = 1, OutputPerMillion = 4 } }, Currency = "EUR" });

        var estimate = cost.Estimate([new("kagent/m", 1_000_000, 500_000), new("kagent/free", 10, 0), new("kagent/idle", 0, 0)]);

        Assert.True(estimate.Priced);
        Assert.Equal(3m, estimate.Amount);
        Assert.Equal("EUR", estimate.Currency);
        Assert.Equal(["kagent/free"], estimate.UnpricedModels);
    }

    [Fact]
    public void AReopenedConversation_CountsWhatWasSent_AndSumsEachModelCall()
    {
        var image = Convert.ToBase64String(new byte[300]);
        JsonElement Event(object data) => JsonSerializer.SerializeToElement(new { data = JsonSerializer.Serialize(data) });
        var events = new[]
        {
            Event(new { content = new { role = "user", parts = new object[] { new { text = new string('q', 80) }, new { inlineData = new { mimeType = "image/png", data = image } } } }, timestamp = 1.0 }),
            // kagent reports each model call on its own (TOK/kagent-usage.md); tool-use input and reasoning are split as A2AUsage splits them.
            Event(new { content = new { role = "model", parts = new object[] { new { functionCall = new { id = "t1", name = "lookup", args = new { } } } } },
                usageMetadata = new { promptTokenCount = 400, toolUsePromptTokenCount = 30, candidatesTokenCount = 20, thoughtsTokenCount = 5, totalTokenCount = 455 }, timestamp = 2.0 }),
            Event(new { content = new { role = "model", parts = new object[] { new { text = "Tokyo." } } },
                usageMetadata = new { promptTokenCount = 480, candidatesTokenCount = 8, totalTokenCount = 500 }, timestamp = 3.0 })
        };

        var (_, usage, sent) = SessionEvents.Read(events, Estimator);

        Assert.Equal(20 + Estimator.Count(image), sent);
        Assert.Equal(new ChatUsage(910, 25 + 20, 955), usage);
    }

    [Fact]
    public void OneCallsUsage_WithoutTheSplitFields_IsPromptAndTheRest()
    {
        var usage = SessionEvents.Usage(JsonNode.Parse("""{"promptTokenCount":433,"candidatesTokenCount":32,"totalTokenCount":465}"""));
        Assert.Equal(new ChatUsage(433, 32, 465), usage);
    }
}
