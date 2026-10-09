namespace Skanyxx.Core.Platform.Memory;

/// <summary>One row of the library list: the card without body and source.</summary>
public sealed record LibraryEntry(
    string Scope, string Key, int Version, string Type, string What, string Why, string Who, DateTime UpdatedAt, string Status);
