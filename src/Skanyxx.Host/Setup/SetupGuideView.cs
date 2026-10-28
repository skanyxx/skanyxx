namespace Skanyxx.Host.Setup;

/// <summary><c>GET /api/setup/guides/{id}</c>: the guide, the detected state (names only) and the agent prompt.</summary>
public sealed record SetupGuideView(SetupGuide Guide, bool Ready, SetupState State, string AgentPrompt);
