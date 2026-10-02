using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Models;

internal sealed class ListModelsHandler(AxGateway ax, ILogger<ListModelsHandler> logger)
    : IRequestHandler<ListModelsQuery, Outcome<IReadOnlyList<SandboxModel>>>
{
    public Task<Outcome<IReadOnlyList<SandboxModel>>> Handle(ListModelsQuery query, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () => Outcome<IReadOnlyList<SandboxModel>>.Ok(
            (await ax.ListModelsAsync(ct)).Select(AxMapper.ToSandboxModel).ToList()));
}
