using MediatR;
using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.CloudTools.Handlers;

public class GetClusterStatusHandler : IRequestHandler<GetClusterStatusQuery, List<SystemStatus>>
{
    private readonly IKubernetesService _svc;
    public GetClusterStatusHandler(IKubernetesService svc) => _svc = svc;
    public Task<List<SystemStatus>> Handle(GetClusterStatusQuery req, CancellationToken ct) => _svc.GetClusterStatusAsync();
}
