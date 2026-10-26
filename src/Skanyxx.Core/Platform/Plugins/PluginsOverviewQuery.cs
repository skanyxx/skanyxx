using MediatR;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Plugins;

/// <summary>The plugins installed in main, their servers' state and agents, and the open plugin proposals.</summary>
public sealed record PluginsOverviewQuery(StudioUser User) : IRequest<Outcome<PluginsOverview>>;
