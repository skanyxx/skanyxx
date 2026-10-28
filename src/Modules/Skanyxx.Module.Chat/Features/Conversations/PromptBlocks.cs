using System.Text.RegularExpressions;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// What Skanyxx puts in front of a person's message: the chat index's related-work block and memory's related cards
/// (first turn, D270), then the files sent with it. kagent's history keeps them; <see cref="Split"/> takes them off
/// again, so a reopened message reads as the person wrote it, with its files as attachments.
/// </summary>
internal static partial class PromptBlocks
{
    private static readonly (string Start, string End)[] Context =
    [
        ("[Related prior work, found by Skanyxx", "[End of related prior work]"),
        ("[Related memory: published cards", "[End of related memory]")
    ];

    public const string FileEnd = "[End of attached file]";

    public static string File(string name, string id, string body) =>
        $"[Attached file \"{name}\" ({id})]\n{body.Replace(FileEnd, "(End of attached file)")}\n{FileEnd}\n\n";

    public static (string Text, IReadOnlyList<ChatAttachment> Files) Split(string text)
    {
        foreach (var (start, end) in Context)
        {
            var at = text.StartsWith(start, StringComparison.Ordinal) ? text.IndexOf(end, StringComparison.Ordinal) : -1;
            if (at >= 0)
                text = text[(at + end.Length)..].TrimStart();
        }
        var files = new List<ChatAttachment>();
        while (FileStart().Match(text) is { Success: true } m && text.IndexOf(FileEnd, m.Length, StringComparison.Ordinal) is var end and >= 0)
        {
            files.Add(new ChatAttachment(m.Groups[2].Value, m.Groups[1].Value));
            text = text[(end + FileEnd.Length)..].TrimStart();
        }
        return (text, files);
    }

    [GeneratedRegex("""^\[Attached file "([^"\r\n]+)" \(([a-f0-9]{32})\)\]\n""")]
    private static partial Regex FileStart();
}
