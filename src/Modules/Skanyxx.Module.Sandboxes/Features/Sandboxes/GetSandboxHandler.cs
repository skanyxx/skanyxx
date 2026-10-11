using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed class GetSandboxHandler(AxGateway ax, ILogger<GetSandboxHandler> logger) : IRequestHandler<GetSandboxQuery, Outcome<Sandbox>>
{
    public Task<Outcome<Sandbox>> Handle(GetSandboxQuery query, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () => await ax.FindSandboxAsync(query.Name, ct) is { } sandbox
            ? Outcome<Sandbox>.Ok(AxMapper.ToSandbox(sandbox))
            : Outcome<Sandbox>.NotFound($"No sandbox '{query.Name}'."));
}
