using System.Text.Json;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Host.Tests;

/// <summary>D555: a reopened conversation holds the questions and the answers, no tool steps (code review 1, m6).</summary>
public sealed class SessionEventsTests
{
    [Fact]
    public void ToolCallsResultsAndTheTextAroundThem_StayOut_AndTheToolResultIsNoTurn()
    {
        // kagent lists events newest first.
        var events = new[]
        {
            Event(4, "seed", """{"role":"model","parts":[{"text":"Thirty days."}]}""", """{"promptTokenCount":50,"candidatesTokenCount":10,"totalTokenCount":60}"""),
            Event(3, "seed", """{"role":"user","parts":[{"functionResponse":{"id":"c1","name":"memory_search","response":{"result":"found a card"}}}]}"""),
            Event(2, "seed", """{"role":"model","parts":[{"text":"Let me look that up."},{"functionCall":{"id":"c1","name":"memory_search","args":{"q":"refund"}}},{"text":"Searching now."}]}""",
                """{"promptTokenCount":100,"candidatesTokenCount":20,"totalTokenCount":120}"""),
            Event(1, "user", """{"role":"user","parts":[{"text":"What is our refund window?"}]}""")
        };

        var (items, usage, sent) = SessionEvents.Read(events, CharTokenEstimator.Instance);

        Assert.Equal([("user", "What is our refund window?"), ("assistant", "Thirty days.")], items.Select(i => (i.Kind, i.Text)));
        Assert.Equal(180, items[1].Usage!.Total);
        Assert.Equal(180, usage.Total);
        Assert.Equal(CharTokenEstimator.Instance.Count("What is our refund window?"), sent);
        var json = JsonSerializer.Serialize(items);
        Assert.DoesNotContain("memory_search", json);
        Assert.DoesNotContain("Let me look", json);
        Assert.DoesNotContain("Searching now", json);
    }

    private static JsonElement Event(int second, string author, string content, string? usage = null) =>
        JsonDocument.Parse("{\"data\":{\"author\":\"" + author + "\",\"timestamp\":" + (1_800_000_000 + second) + ",\"content\":" + content
            + (usage is null ? "" : ",\"usageMetadata\":" + usage) + "}}").RootElement;
}
