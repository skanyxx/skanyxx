using System.Collections.Concurrent;
using System.Text.Json;
using MediatR;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>The identity audit for tests: every row, its details serialized as identity would store them.</summary>
public sealed class AuditLog : IRequestHandler<RecordAuditCommand>
{
    public ConcurrentQueue<(string Action, string? Actor, string? Target, string Details)> Rows { get; } = new();

    public Task Handle(RecordAuditCommand request, CancellationToken ct)
    {
        Rows.Enqueue((request.Action, request.ActorId, request.TargetId, JsonSerializer.Serialize(request.Details)));
        return Task.CompletedTask;
    }
}
