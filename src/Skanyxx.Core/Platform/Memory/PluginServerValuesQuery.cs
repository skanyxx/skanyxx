using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>A plugin server's stored header values in plaintext, for the reconciler to hand to kagent only.</summary>
public sealed record PluginServerValuesQuery(string Server) : IRequest<Outcome<PluginServerValues>>;
