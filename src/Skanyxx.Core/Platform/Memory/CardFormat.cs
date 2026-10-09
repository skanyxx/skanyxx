namespace Skanyxx.Core.Platform.Memory;

/// <summary>Shapes memory validates, in Core so a page can render the same limits on its inputs.</summary>
public static class CardFormat
{
    /// <summary>Search text; also bounds the number of words a search turns into tsqueries.</summary>
    public const int MaxQueryLength = 500;

    public const int MaxKeyLength = 80;

    /// <summary>A key, unanchored. <c>\-</c> keeps it valid both in .NET and as an HTML <c>pattern</c> (the <c>v</c> flag).</summary>
    public const string KeyPattern = @"[a-z0-9][a-z0-9\-]{0,79}";
}
