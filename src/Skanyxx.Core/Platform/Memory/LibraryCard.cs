namespace Skanyxx.Core.Platform.Memory;

/// <param name="LiftedFrom">The source card as <c>scope/key</c>, when there is one and this person may read it.</param>
/// <param name="LiftTargets">Higher scopes this person may write, for a published card; empty otherwise (D2).</param>
/// <param name="CanRename">Whether this person may write the card's scope (D038).</param>
public sealed record LibraryCard(CardDto Card, string? LiftedFrom, IReadOnlyList<string> LiftTargets, bool CanRename);
