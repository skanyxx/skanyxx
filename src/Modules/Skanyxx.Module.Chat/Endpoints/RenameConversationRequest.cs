namespace Skanyxx.Module.Chat.Endpoints;

internal sealed class RenameConversationRequest
{
    public string Id { get; set; } = "";

    public string? Title { get; set; }
}
