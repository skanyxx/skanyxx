using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Creates the first user, with role <see cref="SkanyxxRoles.Owner"/>, only while no user exists (D025).
/// <paramref name="BootstrapToken"/> is what the caller presented; <paramref name="FromLoopback"/> is decided by the
/// transport (a direct loopback connection with no forwarding headers).
/// </summary>
public sealed record BootstrapOwnerCommand(
    string Email, string Password, string? DisplayName, string? BootstrapToken, bool FromLoopback) : IRequest<Outcome<AccountDto>>;
