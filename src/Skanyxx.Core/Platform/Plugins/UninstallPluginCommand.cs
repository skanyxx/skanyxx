using MediatR;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Plugins;

/// <summary>A pull request removing every file of an installed plugin; after the merge the reconciler removes its agents, then its servers.</summary>
public sealed record UninstallPluginCommand(StudioUser User, string Plugin) : IRequest<Outcome<StudioResult>>;
