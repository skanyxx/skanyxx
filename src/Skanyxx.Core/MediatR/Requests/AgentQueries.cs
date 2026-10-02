using MediatR;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.MediatR.Requests;

public record GetAllAgentsQuery : IRequest<List<Agent>>;
public record GetAgentByIdQuery(string Id) : IRequest<Agent?>;
