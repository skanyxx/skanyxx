using Microsoft.Extensions.Options;
using Skanyxx.Module.ChatContext.Extraction;

namespace Skanyxx.Module.ChatContext.Tests;

/// <summary>D239: the deterministic extractor, no database.</summary>
public sealed class ExtractorTests
{
    private static DeterministicExtractor Extractor(Action<ChatContextOptions>? configure = null)
    {
        var options = new ChatContextOptions();
        configure?.Invoke(options);
        return new DeterministicExtractor(Options.Create(options));
    }

    private static Task<SessionDigest> Extract(string message, string reply, SessionDigest? current = null, Action<ChatContextOptions>? configure = null) =>
        Extractor(configure).ExtractAsync(current ?? SessionDigest.Empty, new ChatTurn(message, reply), CancellationToken.None);

    [Fact]
    public async Task Keywords_AreTheHeaviestWords_WithoutStopWords_ThePersonsWordsWeighingMore()
    {
        var digest = await Extract("How do I refund an invoice for Acme?", "To refund the invoice, open the billing console and refund it.");

        Assert.Equal("refund", digest.Keywords[0]);
        Assert.Contains("invoice", digest.Keywords);
        Assert.Contains("acme", digest.Keywords);
        Assert.DoesNotContain("the", digest.Keywords);
        Assert.DoesNotContain("how", digest.Keywords);
        // Once in the message (3), twice in the answer (1 each).
        Assert.Equal(5, digest.KeywordWeights["refund"]);
    }

    [Fact]
    public async Task Weights_AddUpOverTurns_AndStayBounded()
    {
        var first = await Extract("refund policy", "");
        var second = await Extract("refund again", "", first);
        Assert.Equal(6, second.KeywordWeights["refund"]);

        var many = string.Join(' ', Enumerable.Range(0, 200).Select(i => $"word{i}x"));
        var big = await Extract(many, "", second);
        Assert.Equal(SessionDigest.MaxWeights, big.KeywordWeights.Count);
        Assert.Equal(SessionDigest.MaxKeywords, big.Keywords.Count);
        Assert.Equal("refund", big.Keywords[0]);
    }

    [Fact]
    public async Task TicketKeys_AreFound_StandardsAreNot()
    {
        var digest = await Extract("This is about SDB-58 and OPS-7, encoded as UTF-8 with SHA-256 per ISO-8601.", "See also SDB-64.");

        Assert.Equal(["SDB-58", "OPS-7", "SDB-64"], digest.TicketKeys);
    }

    [Fact]
    public async Task TicketProjects_LimitWhichKeysCount()
    {
        var digest = await Extract("SDB-58 and OPS-7", "", configure: o => o.TicketProjects = ["OPS"]);

        Assert.Equal(["OPS-7"], digest.TicketKeys);
    }

    [Fact]
    public async Task Categories_ComeFromTheKeywords()
    {
        var digest = await Extract("The deployment failed with an error after the helm release", "Roll back the release.");

        Assert.Contains("deployment", digest.Categories);
        Assert.Contains("incident", digest.Categories);
        Assert.DoesNotContain("billing", digest.Categories);
        Assert.True(digest.Categories.Count <= SessionDigest.MaxCategories);
    }

    [Fact]
    public async Task ConfiguredCategories_ReplaceTheBuiltInOnes()
    {
        var digest = await Extract("refund the warehouse order", "", configure: o => o.Categories = new() { ["logistics"] = ["warehouse"] });

        Assert.Equal(["logistics"], digest.Categories);
    }

    [Fact]
    public void Outcome_IsTheAnswersOpening_AsPlainText_Bounded()
    {
        Assert.Equal("We refund within 14 days.", DeterministicExtractor.Outcome("We refund within **14** days."));
        Assert.Equal("Use this. Then that.", DeterministicExtractor.Outcome("# Use this.\n```sql\nDROP TABLE x;\n```\nThen that. And more. And more."));
        Assert.Equal(400, DeterministicExtractor.Outcome(new string('a', 2_000)).Length);
    }

    [Fact]
    public async Task AnEmptyAnswer_KeepsTheEarlierOutcome()
    {
        var digest = await Extract("hi", "   ", SessionDigest.Empty with { Outcome = "Earlier result." });

        Assert.Equal("Earlier result.", digest.Outcome);
    }
}
