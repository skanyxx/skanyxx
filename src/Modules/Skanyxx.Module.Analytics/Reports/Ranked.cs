namespace Skanyxx.Module.Analytics.Reports;

/// <summary>A person, team or agent in a top list.</summary>
public sealed record Ranked(string Id, string Name, long Tokens, long Requests, long Chats);
