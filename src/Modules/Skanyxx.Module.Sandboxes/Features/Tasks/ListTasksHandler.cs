using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class ListTasksHandler(AxGateway ax, ILogger<ListTasksHandler> logger)
    : IRequestHandler<ListTasksQuery, Outcome<IReadOnlyList<SandboxTask>>>
{
    public Task<Outcome<IReadOnlyList<SandboxTask>>> Handle(ListTasksQuery query, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () => Outcome<IReadOnlyList<SandboxTask>>.Ok(
            (await ax.ListTasksAsync(query.Limit, query.Offset, ct)).Select(AxMapper.ToSandboxTask).ToList()));
}
