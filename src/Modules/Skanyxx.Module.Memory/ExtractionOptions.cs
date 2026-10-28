using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Module.Memory;

/// <summary><c>Memory:Extraction</c>: which finished work becomes a personal card.</summary>
public sealed class ExtractionOptions
{
    public bool Chats { get; set; } = true;

    public bool Runs { get; set; } = true;

    /// <summary>A new chat whose first message has fewer words (and names no ticket) keeps no card: "hi" is not work.</summary>
    [Range(1, 50)]
    public int MinWords { get; set; } = 3;
}
