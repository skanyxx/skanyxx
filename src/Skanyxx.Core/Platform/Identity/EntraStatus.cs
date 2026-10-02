namespace Skanyxx.Core.Platform.Identity;

/// <summary><paramref name="Enabled"/>: "Sign in with Microsoft" is offered. <paramref name="Linked"/>: the asked-about account has a Microsoft login.</summary>
public sealed record EntraStatus(bool Enabled, bool Linked);
