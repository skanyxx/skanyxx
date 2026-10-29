namespace Skanyxx.Module.Investigate.Rules;

/// <summary>
/// What a viewer reaches beyond their own work, read live from the org tree (D371): <see cref="Teams"/> they are in
/// (workflows shared there), and for a supervisor <see cref="People"/>, the members of those teams other than the owner
/// and supervisors (D386): their workflows and runs, the runs' input and answers included. The owner's reach is
/// everything and needs neither.
/// </summary>
public sealed record Reach(IReadOnlySet<string> Teams, IReadOnlySet<string> People)
{
    public static Reach None { get; } = new(new HashSet<string>(), new HashSet<string>());
}
