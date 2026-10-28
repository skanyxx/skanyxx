namespace Skanyxx.Module.Chat.Features.Conversations;

internal static class ConversationItemKinds
{
    public const string User = "user";

    /// <summary>The turn's answer.</summary>
    public const string Assistant = "assistant";

    /// <summary>What the model said on its way to a tool call (D281: not the answer; shown with the steps).</summary>
    public const string Step = "step";

    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
}
