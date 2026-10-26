using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>Reads a source and says what installing it would propose; nothing is written anywhere.</summary>
internal sealed record InspectPluginQuery(StudioUser User, PluginSourceRequest Request, PluginUpload? Upload) : IRequest<Outcome<PluginInspection>>;
