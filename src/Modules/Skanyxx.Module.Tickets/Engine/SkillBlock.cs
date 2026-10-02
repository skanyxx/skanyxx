namespace Skanyxx.Module.Tickets.Engine;

/// <summary>A titled block of the stage prompt, plus anything a person should know about how it was built.</summary>
public sealed record SkillBlock(string Title, string Content, IReadOnlyList<string> Warnings)
{
    public SkillBlock(string title, string content) : this(title, content, []) { }
}
