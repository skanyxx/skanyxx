using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class GetTaskHandler(AxGateway ax, ILogger<GetTaskHandler> logger) : IRequestHandler<GetTaskQuery, Outcome<SandboxTask>>
{
    public Task<Outcome<SandboxTask>> Handle(GetTaskQuery query, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () => await ax.FindTaskAsync(query.Name, ct) is { } task
            ? Outcome<SandboxTask>.Ok(AxMapper.ToSandboxTask(task))
            : Outcome<SandboxTask>.NotFound($"No task '{query.Name}'."));
}
