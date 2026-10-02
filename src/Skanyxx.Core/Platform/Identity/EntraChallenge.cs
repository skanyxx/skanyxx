using Microsoft.AspNetCore.Authentication;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>What a page hands to <c>Challenge</c> to send the browser to Microsoft.</summary>
public sealed record EntraChallenge(string Scheme, AuthenticationProperties Properties);
