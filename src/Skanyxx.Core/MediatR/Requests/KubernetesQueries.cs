using MediatR;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.MediatR.Requests;

public record GetClusterStatusQuery : IRequest<List<SystemStatus>>;
