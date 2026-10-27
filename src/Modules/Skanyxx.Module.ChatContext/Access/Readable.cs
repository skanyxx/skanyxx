namespace Skanyxx.Module.ChatContext.Access;

/// <summary>
/// The sessions one person may read: their own (any scope), and those shared to one of <paramref name="Scopes"/> (never a personal one), or to
/// any team or department when <paramref name="AllOrg"/> (supervisor oversight). Never another person's personal session.
/// </summary>
internal sealed record Readable(string UserId, IReadOnlyList<string> Scopes, bool AllOrg);
