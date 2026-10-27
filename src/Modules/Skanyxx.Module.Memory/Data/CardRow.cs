using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Contracts;

namespace Skanyxx.Module.Memory.Data;

/// <summary>
/// One search row. Dapper matches record constructors by the reader's field types, and Npgsql reports a text[] column
/// as <see cref="Array"/>, so array columns bind through settable properties instead.
/// </summary>
internal sealed class CardRow
{
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public int Version { get; set; }
    public string Type { get; set; } = "";
    public string What { get; set; } = "";
    public string Why { get; set; } = "";
    public string Who { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string Status { get; set; } = "";
    public string[] Tickets { get; set; } = [];
    public string Origin { get; set; } = "";

    public CardHit ToHit() => new(Scope, Key, Version, Type, What, Why, Who, UpdatedAt, Tickets);

    public LibraryEntry ToEntry() => new(Scope, Key, Version, Type, What, Why, Who, UpdatedAt, Status, Tickets, Origin);
}
