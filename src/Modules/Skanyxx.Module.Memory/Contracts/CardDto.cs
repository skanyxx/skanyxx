namespace Skanyxx.Module.Memory.Contracts;

/// <summary>What a human sees in the library: the card plus body and source pointer.</summary>
public sealed record CardDto(
    string Scope, string Key, int Version, string Type, string What, string Why, string Who, DateTime UpdatedAt,
    string Status, string? Body, string? Source, long? LiftedFromId);
