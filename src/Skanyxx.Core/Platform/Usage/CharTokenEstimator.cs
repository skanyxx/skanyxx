namespace Skanyxx.Core.Platform.Usage;

/// <summary>The default <see cref="ITokenEstimator"/>: <see cref="CharsPerToken"/> characters a token, rounded up, for every model.</summary>
public sealed class CharTokenEstimator : ITokenEstimator
{
    public const int CharsPerToken = 4;

    public static readonly CharTokenEstimator Instance = new();

    public long Count(string text, string? model = null) => Tokens(text.Length);

    public long CountBase64(long bytes, string? model = null) => Tokens((bytes + 2) / 3 * 4);

    /// <summary>The characters <paramref name="tokens"/> tokens hold: a budget in tokens as a budget in characters.</summary>
    public static int Chars(int tokens) => tokens * CharsPerToken;

    private static long Tokens(long chars) => (chars + CharsPerToken - 1) / CharsPerToken;
}
