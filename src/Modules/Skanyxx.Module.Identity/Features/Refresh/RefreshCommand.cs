using MediatR;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features.Refresh;

public sealed record RefreshCommand(string RefreshToken) : IRequest<Outcome<AccessTokenResponse>>;
