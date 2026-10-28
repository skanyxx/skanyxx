using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Runtime.Models.Features;

/// <param name="InUseConfirmed">The owner saw the agents that use the model and deletes it anyway.</param>
internal sealed record DeleteModelConfigCommand(string ActorId, string Namespace, string Name, bool InUseConfirmed) : IRequest<Outcome<ModelDeletion>>;
