using MediatR;

namespace Skanyxx.Core.Platform.Studio;

public sealed record StudioFormOptionsQuery(StudioUser User) : IRequest<Outcome<StudioFormOptions>>;
