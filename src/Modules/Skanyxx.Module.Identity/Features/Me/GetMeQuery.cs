using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Features.Me;

public sealed record GetMeQuery(string UserId) : IRequest<Outcome<AccountDto>>;
