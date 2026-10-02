using MediatR;

namespace Skanyxx.Core.Platform.Identity;

public sealed record GetEntraSettingsQuery : IRequest<Outcome<EntraSettingsDto>>;
