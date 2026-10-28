using System.Text.Json.Nodes;
using Skanyxx.Core.Services;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>The tokens a blocking A2A answer reports, as kagent's runtimes put them in event metadata.</summary>
public sealed class A2AUsageTests
{
    private static JsonNode Usage(long prompt, long candidates) =>
        new JsonObject { ["promptTokenCount"] = prompt, ["candidatesTokenCount"] = candidates, ["totalTokenCount"] = prompt + candidates };

    [Fact]
    public void NoMetadata_IsNotReported()
    {
        var usage = A2AUsage.From(JsonNode.Parse("""{"kind":"task","status":{"state":"completed"},"artifacts":[{"parts":[]}]}"""));

        Assert.False(usage.Reported);
        Assert.Equal(0, usage.InputTokens + usage.OutputTokens);
    }

    [Fact]
    public void EveryModelCall_CountsOnce_AMessageInHistoryAndStatusOnlyOnce()
    {
        var result = new JsonObject
        {
            ["kind"] = "task",
            ["history"] = new JsonArray(
                new JsonObject { ["messageId"] = "m1", ["metadata"] = new JsonObject { ["kagent_usage_metadata"] = Usage(100, 20) } },
                new JsonObject { ["messageId"] = "m2", ["metadata"] = new JsonObject { ["kagent_usage_metadata"] = Usage(150, 30) } }),
            ["status"] = new JsonObject
            {
                ["state"] = "completed",
                ["message"] = new JsonObject { ["messageId"] = "m2", ["metadata"] = new JsonObject { ["kagent_usage_metadata"] = Usage(150, 30) } }
            },
            // The task's own copy is the last call's: ignored once the messages carry theirs.
            ["metadata"] = new JsonObject { ["kagent_usage_metadata"] = Usage(150, 30) }
        };

        var usage = A2AUsage.From(result);

        Assert.True(usage.Reported);
        Assert.Equal(250, usage.InputTokens);
        Assert.Equal(50, usage.OutputTokens);
    }

    [Fact]
    public void TaskMetadata_IsTheFallback_GoKeysAndReasoningTokensCount()
    {
        var result = new JsonObject
        {
            ["kind"] = "task",
            ["metadata"] = new JsonObject
            {
                ["adk_usage_metadata"] = new JsonObject
                {
                    ["prompt_token_count"] = 40, ["tool_use_prompt_token_count"] = 2, ["candidates_token_count"] = 7, ["thoughts_token_count"] = 3
                }
            }
        };

        var usage = A2AUsage.From(result);

        Assert.Equal(42, usage.InputTokens);
        Assert.Equal(10, usage.OutputTokens);
    }

    [Theory]
    [InlineData("""{"kind":"task","artifacts":["x"],"status":"done","history":[3]}""")]
    [InlineData("""["not","an","object"]""")]
    [InlineData("""42""")]
    public void ShapesOfAnotherKind_AreNoUsage(string json) => Assert.False(A2AUsage.From(JsonNode.Parse(json)).Reported);

    [Fact]
    public void Artifacts_CountOnlyWhenNoMessageReported()
    {
        var result = new JsonObject
        {
            ["kind"] = "task",
            ["history"] = new JsonArray(new JsonObject { ["messageId"] = "m1", ["metadata"] = new JsonObject { ["kagent_usage_metadata"] = Usage(10, 2) } }),
            ["artifacts"] = new JsonArray(new JsonObject { ["metadata"] = new JsonObject { ["kagent_usage_metadata"] = Usage(10, 2) } })
        };

        Assert.Equal(10, A2AUsage.From(result).InputTokens);
        result.Remove("history");
        Assert.Equal(10, A2AUsage.From(result).InputTokens);
    }

    [Fact]
    public void OddShapes_AreNotTokens_AndNeverThrow()
    {
        var usage = A2AUsage.From(JsonNode.Parse("""
            {"kind":"task","history":[{"messageId":7,"metadata":{"kagent_usage_metadata":{"promptTokenCount":"many","candidatesTokenCount":-5}}}]}
            """));

        Assert.True(usage.Reported);
        Assert.Equal(0, usage.InputTokens + usage.OutputTokens);
    }
}
