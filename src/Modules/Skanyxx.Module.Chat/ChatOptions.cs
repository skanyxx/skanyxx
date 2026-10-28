namespace Skanyxx.Module.Chat;

public sealed class ChatOptions
{
    public const string Section = "Chat";

    public ChatFileOptions Files { get; set; } = new();
}
