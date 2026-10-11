using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

/// <summary>Stands in for the memory module's token handlers (D076): records what the sandboxes module asked for.</summary>
public sealed class FakeMemoryTokens
    : IRequestHandler<IssueSandboxMemoryTokenCommand, Outcome<SandboxMemoryToken>>, IRequestHandler<RevokeSandboxMemoryTokenCommand, Outcome<bool>>
{
    public const string Url = "http://skanyxx-sandbox-mcp.skanyxx.svc:8082/mcp/memory";

    public ConcurrentQueue<IssueSandboxMemoryTokenCommand> Issued { get; } = new();
    public ConcurrentQueue<string> Revoked { get; } = new();
    public OutcomeStatus IssueStatus { get; set; } = OutcomeStatus.Ok;
    public bool RevokeFails { get; set; }

    public static string TokenFor(string agentId, int n) => $"skx_sbx_token-{n}-for-{agentId}";

    public Task<Outcome<SandboxMemoryToken>> Handle(IssueSandboxMemoryTokenCommand request, CancellationToken ct)
    {
        Issued.Enqueue(request);
        return Task.FromResult(IssueStatus == OutcomeStatus.Ok
            ? Outcome<SandboxMemoryToken>.Ok(new(request.AgentId, TokenFor(request.AgentId, Issued.Count), DateTimeOffset.UtcNow.AddHours(1)))
            : new Outcome<SandboxMemoryToken>(IssueStatus, Message: "memory said no"));
    }

    public Task<Outcome<bool>> Handle(RevokeSandboxMemoryTokenCommand request, CancellationToken ct)
    {
        if (RevokeFails)
            return Task.FromResult(Outcome<bool>.Unavailable("memory is down"));
        Revoked.Enqueue(request.AgentId);
        return Task.FromResult(Outcome<bool>.Ok(true));
    }

    /// <summary>The settings that turn memory attach on (D077): the URL on the sandbox listener's port, and the key.</summary>
    public static void Configure(Dictionary<string, string?> settings)
    {
        settings["Sandboxes:MemoryMcpUrl"] = Url;
        settings["Memory:SandboxMcpPort"] = "8082";
        settings["Memory:SandboxTokenKey"] = "a-sandbox-token-key-of-at-least-32-characters";
    }

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IRequestHandler<IssueSandboxMemoryTokenCommand, Outcome<SandboxMemoryToken>>>(this);
        services.AddSingleton<IRequestHandler<RevokeSandboxMemoryTokenCommand, Outcome<bool>>>(this);
    }
}
