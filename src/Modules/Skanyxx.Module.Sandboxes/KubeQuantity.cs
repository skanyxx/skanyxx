using System.Globalization;
using System.Text.RegularExpressions;

namespace Skanyxx.Module.Sandboxes;

/// <summary>Kubernetes quantities (<c>500m</c>, <c>2</c>, <c>1Gi</c>) as numbers. At most 12 integer digits, so no suffix overflows a decimal.</summary>
internal static partial class KubeQuantity
{
    public static bool TryParse(string? value, out decimal amount)
    {
        amount = 0;
        var match = value is null ? Match.Empty : Pattern().Match(value);
        if (!match.Success)
            return false;
        amount = decimal.Parse(match.Groups["n"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
                 * Multiplier(match.Groups["s"].Value);
        return true;
    }

    /// <summary>Both parse and <paramref name="a"/> is larger.</summary>
    public static bool Exceeds(string? a, string? b) => TryParse(a, out var x) && TryParse(b, out var y) && x > y;

    public static string Max(string a, string b) => Exceeds(b, a) ? b : a;

    public static string Min(string a, string? b) => Exceeds(a, b) ? b! : a;

    /// <summary>Every value parses and none exceeds the next.</summary>
    public static bool IsOrdered(params string[] values) =>
        values.All(v => TryParse(v, out _)) && values.Zip(values.Skip(1)).All(p => !Exceeds(p.First, p.Second));

    private static decimal Multiplier(string suffix) => suffix switch
    {
        "" => 1m,
        "m" => 0.001m,
        "k" => 1e3m,
        "M" => 1e6m,
        "G" => 1e9m,
        "T" => 1e12m,
        "Ki" => 1024m,
        "Mi" => 1024m * 1024,
        "Gi" => 1024m * 1024 * 1024,
        _ => 1024m * 1024 * 1024 * 1024 // Ti
    };

    [GeneratedRegex(@"^(?<n>[0-9]{1,12}(\.[0-9]{1,9})?)(?<s>m|k|M|G|T|Ki|Mi|Gi|Ti)?\z")]
    private static partial Regex Pattern();
}
