using MediatR;

namespace Skanyxx.Core.Platform.Runtime;

/// <summary>The default model (<c>KAgent:ModelConfig</c>) read from kagent; null while it does not exist, Unavailable when kagent does not answer.</summary>
public sealed record DefaultModelQuery : IRequest<Outcome<ModelConfigDetail?>>;
