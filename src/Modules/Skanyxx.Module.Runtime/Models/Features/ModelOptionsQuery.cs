using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Runtime.Models.Features;

internal sealed record ModelOptionsQuery : IRequest<Outcome<ModelFormOptions>>;
