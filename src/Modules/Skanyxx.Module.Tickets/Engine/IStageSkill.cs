namespace Skanyxx.Module.Tickets.Engine;

/// <summary>A deterministic prompt block. Adding one is one class plus one DI registration.</summary>
public interface IStageSkill
{
    string Key { get; }
    string Description { get; }
    SkillBlock Run(SkillContext context);
}
