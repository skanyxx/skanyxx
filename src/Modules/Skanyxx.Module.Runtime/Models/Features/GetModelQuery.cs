using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.Runtime.Models.Features;

internal sealed record GetModelQuery(string Namespace, string Name) : IRequest<Outcome<ModelConfigDetail>>;
