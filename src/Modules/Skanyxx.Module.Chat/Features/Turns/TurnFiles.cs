using Skanyxx.Core.Models;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>A turn's files: text (redacted) in <paramref name="Block"/>, images as A2A file parts.</summary>
internal sealed record TurnFiles(string Block, IReadOnlyList<A2AFilePart> Images, IReadOnlyList<StoredChatFile> Stored)
{
    public static readonly TurnFiles None = new("", [], []);
}
