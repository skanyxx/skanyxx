using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Contracts;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>Sets (null: clears) the monthly limit on a person, team or department.</summary>
public sealed record SetLimitCommand(QuotaViewer Viewer, string Kind, string Id, long? MonthlyTokens) : IRequest<Outcome<LimitResult>>;
