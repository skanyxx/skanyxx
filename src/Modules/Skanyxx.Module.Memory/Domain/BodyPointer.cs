using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// D181: a card whose body is in the object store has <c>source = s3://{bucket}/cards/sha256/{hex}</c>, the SHA-256 of the
/// body's UTF-8 bytes. Only memory writes this shape; a caller cannot set it (D182).
/// </summary>
public static partial class BodyPointer
{
    public const string KeyPrefix = "cards/sha256/";

    public static string Format(string bucket, string key) => $"s3://{bucket}/{key}";

    public static bool IsPointer([NotNullWhen(true)] string? source) => source is not null && Shape().IsMatch(source);

    public static bool TryParse(string? source, [NotNullWhen(true)] out string? bucket, [NotNullWhen(true)] out string? key)
    {
        var match = source is null ? Match.Empty : Shape().Match(source);
        bucket = match.Success ? match.Groups["bucket"].Value : null;
        key = match.Success ? match.Groups["key"].Value : null;
        return match.Success;
    }

    [GeneratedRegex("^s3://(?<bucket>[a-z0-9][a-z0-9.-]{1,61}[a-z0-9])/(?<key>cards/sha256/[0-9a-f]{64})$")]
    private static partial Regex Shape();
}
