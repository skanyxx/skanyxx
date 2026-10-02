using MediatR;
using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.Agents.Handlers;

public class GetAllAgentsHandler : IRequestHandler<GetAllAgentsQuery, List<Agent>>
{
    private readonly IAgentService _svc;
    public GetAllAgentsHandler(IAgentService svc) => _svc = svc;
    public Task<List<Agent>> Handle(GetAllAgentsQuery req, CancellationToken ct) => _svc.GetAllAsync();
}

public class GetAgentByIdHandler : IRequestHandler<GetAgentByIdQuery, Agent?>
{
    private readonly IAgentService _svc;
    public GetAgentByIdHandler(IAgentService svc) => _svc = svc;
    public Task<Agent?> Handle(GetAgentByIdQuery req, CancellationToken ct) => _svc.GetByIdAsync(req.Id);
}
