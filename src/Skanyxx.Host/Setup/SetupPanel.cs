namespace Skanyxx.Host.Setup;

/// <summary>What the shared setup component renders for one guide and one viewer.</summary>
/// <param name="CanRead">The viewer may read infrastructure detail (<see cref="SetupAccess"/>).</param>
public sealed record SetupPanel(SetupGuide Guide, SetupState State, bool CanRead)
{
    public string Prompt => AgentPrompt.For(Guide, State);
}
