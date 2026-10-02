namespace Skanyxx.Module.Tickets.Engine;

public sealed record StagePrompt(string Text, IReadOnlyList<string> Warnings);
