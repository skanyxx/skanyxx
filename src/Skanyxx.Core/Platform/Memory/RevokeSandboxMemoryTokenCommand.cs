using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>D076: the sandbox stopped (or no longer needs memory); its token stops working at once. Ok(false): none was live.</summary>
public sealed record RevokeSandboxMemoryTokenCommand(string AgentId) : IRequest<Outcome<bool>>;
