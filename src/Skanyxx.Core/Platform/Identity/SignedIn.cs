using Microsoft.AspNetCore.Authentication.BearerToken;

namespace Skanyxx.Core.Platform.Identity;

/// <summary><see cref="Tokens"/> is null for a cookie sign-in.</summary>
public sealed record SignedIn(AccountDto User, AccessTokenResponse? Tokens);
