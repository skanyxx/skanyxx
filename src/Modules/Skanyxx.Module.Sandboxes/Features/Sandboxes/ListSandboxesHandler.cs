using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed class ListSandboxesHandler(AxGateway ax, ILogger<ListSandboxesHandler> logger)
    : IRequestHandler<ListSandboxesQuery, Outcome<IReadOnlyList<Sandbox>>>
{
    public Task<Outcome<IReadOnlyList<Sandbox>>> Handle(ListSandboxesQuery query, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () => Outcome<IReadOnlyList<Sandbox>>.Ok(
            (await ax.ListSandboxesAsync(query.Limit, query.Offset, ct)).Select(AxMapper.ToSandbox).ToList()));
}
