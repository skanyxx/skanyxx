using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Module.Memory;

/// <summary>
/// <c>Memory:Dragonfly</c>: the umbrella's one Dragonfly (D068), used by memory for a search cache and for write limits
/// shared by every replica — never for cards (D042). No <see cref="Configuration"/> = off: limits stay per process and
/// nothing is cached. A Dragonfly that stops answering is the same as off until it answers again.
/// </summary>
public sealed class DragonflyOptions
{
    /// <summary>A StackExchange.Redis configuration string, e.g. <c>skanyxx-dragonfly:6379,password=…</c>.</summary>
    public string? Configuration { get; set; }

    /// <summary>How long a search result is kept; any write to one of its scopes ends it sooner.</summary>
    [Range(1, 3_600)]
    public int SearchCacheSeconds { get; set; } = 60;

    /// <summary>Every key memory writes starts with this, so it shares the instance with AX without meeting its keys.</summary>
    [RegularExpression(@"^[a-z0-9:._-]{1,64}$")]
    public string KeyPrefix { get; set; } = "skanyxx:memory:";

    public bool Enabled => !string.IsNullOrWhiteSpace(Configuration);
}
