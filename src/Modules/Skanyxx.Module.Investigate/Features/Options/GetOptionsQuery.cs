using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Options;

public sealed record GetOptionsQuery(Viewer Viewer) : IRequest<Outcome<OptionsDto>>;
