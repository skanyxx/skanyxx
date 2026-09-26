using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>Where a card lives: <c>company</c> or <c>personal|team|department:&lt;id&gt;</c>.</summary>
public readonly record struct Scope(ScopeLevel Level, string? Id)
{
    public const string CompanyName = "company";

    public static readonly Scope Company = new(ScopeLevel.Company, null);

    public static Scope Personal(string userId) => new(ScopeLevel.Personal, userId);

    public static bool TryParse(string? value, out Scope scope)
    {
        scope = default;
        if (value is null)
            return false;
        if (value == CompanyName)
        {
            scope = Company;
            return true;
        }

        var separator = value.IndexOf(':');
        if (separator < 0)
            return false;

        ScopeLevel? level = value[..separator] switch
        {
            "personal" => ScopeLevel.Personal,
            "team" => ScopeLevel.Team,
            "department" => ScopeLevel.Department,
            _ => null
        };
        var id = value[(separator + 1)..];
        if (level is null || !IsValidId(id))
            return false;

        scope = new Scope(level.Value, id);
        return true;
    }

    public static Scope Parse(string value) =>
        TryParse(value, out var scope) ? scope : throw new FormatException($"Invalid scope '{value}'.");

    public static bool IsValidId(string? id) => Identifier.IsValid(id);

    public override string ToString() =>
        Level == ScopeLevel.Company ? CompanyName : $"{Level.ToString().ToLowerInvariant()}:{Id}";
}
