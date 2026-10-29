using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Investigate.Agents;

/// <summary>An agent's answer and the tokens kagent reported for it (<see cref="ChatUsage.None"/> when it reported none).</summary>
public sealed record AgentAnswer(string Text, ChatUsage Usage);
