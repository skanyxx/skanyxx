using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Module.Chat;

/// <summary>Files people attach in Chat (<c>Chat:Files</c>). Kept per person on the Host's disk, never under wwwroot.</summary>
public sealed class ChatFileOptions
{
    /// <summary>The folder, relative to the content root (or absolute). On Kubernetes, a volume.</summary>
    [Required]
    public string Path { get; set; } = "data/chat-files";

    [Range(1, MaxBytes)]
    public int MaxTextBytes { get; set; } = 512 * 1024;

    [Range(1, MaxBytes)]
    public int MaxImageBytes { get; set; } = 4 * 1024 * 1024;

    [Range(1, 20)]
    public int MaxFilesPerMessage { get; set; } = 5;

    /// <summary>All text files of one message together, in characters: the rest is cut (a prompt has a context window).</summary>
    [Range(1_000, 1_000_000)]
    public int MaxTextChars { get; set; } = 60_000;

    /// <summary>The most files one person keeps; a new upload past it is refused until they delete a conversation.</summary>
    [Range(1, 100_000)]
    public int MaxFilesPerPerson { get; set; } = 200;

    /// <summary>A file never sent with a message is deleted after this many hours (at that person's next upload).</summary>
    [Range(1, 24 * 365)]
    public int UnsentHours { get; set; } = 24;

    /// <summary>Sent to the agent as text (UTF-8 only).</summary>
    public string[] TextExtensions { get; set; } =
        [".txt", ".md", ".csv", ".tsv", ".json", ".yaml", ".yml", ".xml", ".log", ".ini", ".toml", ".cs", ".py", ".js", ".ts", ".go", ".java", ".sql", ".sh", ".html", ".css"];

    /// <summary>Sent as images, and only to an agent labelled <c>skanyxx.dev/accepts-images: "true"</c>.</summary>
    public string[] ImageExtensions { get; set; } = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>The upload endpoint's body cap, so no configuration can raise a request above it.</summary>
    public const int MaxBytes = 20 * 1024 * 1024;
}
