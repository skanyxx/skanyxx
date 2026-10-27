namespace Skanyxx.Core.Platform.Memory;

/// <summary>A card by its address, with its one line: what a chat reply lists as the memory it was given.</summary>
public sealed record MemoryCardRef(string Scope, string Key, string What);
