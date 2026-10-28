namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Related cards for a prompt: <paramref name="Block"/> is prepended to the message ("" when none), and
/// <paramref name="Cards"/> are exactly the cards it holds. <paramref name="Tokens"/> is the block's estimated size.
/// </summary>
public sealed record MemoryContext(string Block, IReadOnlyList<MemoryCardRef> Cards, int Tokens)
{
    public static readonly MemoryContext None = new("", [], 0);
}
