namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>What <see cref="RacingSignInManager"/> runs once, after the password check and before the cookie is issued.</summary>
public sealed class SignInRace
{
    public Func<Task>? BeforeCookie { get; set; }

    public bool Ran { get; private set; }

    public async Task RunOnceAsync()
    {
        if (BeforeCookie is not { } action || Ran)
            return;
        Ran = true;
        await action();
    }
}
