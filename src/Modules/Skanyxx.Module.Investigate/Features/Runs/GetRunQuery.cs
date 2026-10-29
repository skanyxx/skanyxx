using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

public sealed record GetRunQuery(Viewer Viewer, Guid Id) : IRequest<Outcome<InvestigationRun>>;
