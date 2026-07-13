using MediatR;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.MediatR.Requests;

public record GetSessionAnalyticsQuery(string Period = "7d") : IRequest<SessionAnalytics>;
