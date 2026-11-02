using System.Text.Json.Nodes;
using Skanyxx.Module.Chat.Features.Turns;

namespace Skanyxx.Host.Tests;

/// <summary>D555: a streamed turn sends no tool steps; what the model said on its way to a tool is taken away, not kept.</summary>
public sealed class A2ATurnTests
{
    [Fact]
    public void AToolCall_SendsNoStep_AndClearsTheShownDraft()
    {
        var turn = new A2ATurn();

        var events = turn.Read(Status("working", Part("Let me look", partial: true))).ToList();
        events.AddRange(turn.Read(Status("working", new JsonObject
        {
            ["kind"] = "data", ["data"] = new JsonObject { ["id"] = "c1", ["name"] = "memory_search", ["args"] = new JsonObject() },
            ["metadata"] = new JsonObject { ["kagent_type"] = "function_call" }
        })));
        events.AddRange(turn.Read(Status("working", new JsonObject
        {
            ["kind"] = "data", ["data"] = new JsonObject { ["id"] = "c1", ["name"] = "memory_search", ["response"] = new JsonObject() },
            ["metadata"] = new JsonObject { ["kagent_type"] = "function_response" }
        })));
        events.AddRange(turn.Read(Status("completed", Part("Thirty days."), final: true)));

        Assert.Equal(["delta", "text", "answer"], events.Select(e => e.Type));
        Assert.Equal("", events[1].Data.GetType().GetProperty("text")!.GetValue(events[1].Data));
        Assert.Equal("Thirty days.", turn.Answer);
    }

    private static JsonObject Part(string text, bool partial = false) => new() { ["kind"] = "text", ["text"] = text, ["partial"] = partial };

    private static JsonObject Status(string state, JsonObject part, bool final = false)
    {
        var partial = part["partial"]?.GetValue<bool>() == true;
        part.Remove("partial");
        return new JsonObject
        {
            ["kind"] = "status-update", ["taskId"] = "t", ["final"] = final,
            ["status"] = new JsonObject
            {
                ["state"] = state,
                ["message"] = new JsonObject
                {
                    ["role"] = "agent", ["messageId"] = Guid.NewGuid().ToString(), ["parts"] = new JsonArray(part),
                    ["metadata"] = partial ? new JsonObject { ["adk_partial"] = true } : null
                }
            }
        };
    }
}
