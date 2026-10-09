using MediatR;

namespace Skanyxx.Core.Platform.Runtime;

/// <summary>The current model settings, read from kagent (Unavailable when kagent does not answer).</summary>
public sealed record ModelSettingsQuery : IRequest<Outcome<ModelSettings>>;
