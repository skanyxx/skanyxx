using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Module.Memory;

/// <summary><c>Memory:Injection</c>: the related-cards block on a new chat's first message (requirements.md O1).</summary>
public sealed class InjectionOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>The block's budget, estimated at 4 characters a token, header included. A card that does not fit is left out whole.</summary>
    [Range(100, 4_000)]
    public int MaxTokens { get; set; } = 600;

    /// <summary>At most this many cards, best match first (D037: start at 5).</summary>
    [Range(1, 20)]
    public int TopK { get; set; } = 5;
}
