using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>Reads the source again and, when it is still what was inspected (<see cref="PluginSourceRequest.Digest"/>), proposes it.</summary>
internal sealed record InstallPluginSourceCommand(StudioUser User, PluginSourceRequest Request, PluginUpload? Upload) : IRequest<Outcome<StudioResult>>;
