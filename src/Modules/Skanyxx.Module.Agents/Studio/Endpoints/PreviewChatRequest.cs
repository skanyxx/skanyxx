namespace Skanyxx.Module.Agents.Studio.Endpoints;

internal sealed class PreviewChatRequest
{
    public int Number { get; set; }

    public string? Message { get; set; }

    public string? ConversationId { get; set; }
}
