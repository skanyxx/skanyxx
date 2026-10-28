namespace Skanyxx.Module.Memory.Domain;

/// <summary>D037: the token budget is a product rule.</summary>
public static class CardLimits
{
    public const int What = 200;
    public const int Why = 400;
    public const int Body = 20_000;
    public const int Source = 2_048;

    /// <summary>What Postgres keeps of a body that went to the object store (D181): its first characters.</summary>
    public const int BodyExcerpt = 500;

    public const int Tickets = 20;
    public const int Links = 20;
    public const int LinkLength = 512;
    public const int Keywords = 12;
    public const int KeywordLength = 40;
}
