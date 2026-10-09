using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Browser sign-in (session cookie): password, or "Sign in with Microsoft" when the owner turned it on (D027). Before
/// the owner exists, sends the visitor to setup instead. The Microsoft flow starts with a POST (antiforgery, counted in
/// the sign-in rate-limit window) and comes back to <see cref="OnGetMicrosoftAsync"/> after <c>/signin-oidc</c>.
/// </summary>
[AllowAnonymous]
public sealed class LoginModel(IMediator mediator) : PageModel
{
    // Nullable: MVC binds an empty or missing field as null.
    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    /// <summary>Optional break-glass: the owner with <c>Identity:BootstrapToken</c> signs in past a lockout (D081).</summary>
    [BindProperty]
    public string? BootstrapToken { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    /// <summary>
    /// Shown whenever the Microsoft button is: an Entra-managed account's password is answered like a wrong one while
    /// Microsoft sign-in is on (SEC N3), so the page points everyone at the button instead of naming the account.
    /// </summary>
    public const string MicrosoftHint = "If your organisation uses Microsoft sign-in, use the button below.";

    /// <summary>"Sign in with Microsoft" is offered (D6: hidden while off).</summary>
    public bool MicrosoftSignIn { get; private set; }

    /// <summary>"Forgot your password?" is offered: SMTP is configured (D156).</summary>
    public bool PasswordResetByEmail { get; private set; }

    /// <summary>Back from the reset page: the password was changed.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Reset { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var status = (await mediator.Send(new IdentityStatusQuery(), ct)).Value!;
        if (!status.Bootstrapped)
            return RedirectToPage("/Setup");
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl);
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostMicrosoftAsync(CancellationToken ct)
    {
        var challenge = await mediator.Send(new EntraChallengeQuery(Url.Page("/Login", "Microsoft", new { returnUrl = SafeReturnUrl })!), ct);
        if (challenge.Value is { } started)
            return Challenge(started.Properties, started.Scheme);
        if (challenge.Status == OutcomeStatus.NotFound)
            return NotFound();
        Error = challenge.Message;
        Response.StatusCode = challenge.Status.HttpStatus();
        await LoadAsync(ct);
        return Page();
    }

    /// <summary>Where the Microsoft sign-in comes back: a session and the return URL, or the page with the refusal.</summary>
    public async Task<IActionResult> OnGetMicrosoftAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new CompleteEntraSignInCommand(), ct);
        if (outcome.Value is not null)
            return LocalRedirect(SafeReturnUrl);
        Error = outcome.Message;
        Response.StatusCode = outcome.Status.HttpStatus();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await mediator.Send(new SignInCommand(Email?.Trim() ?? "", Password ?? "", UseCookie: true,
                string.IsNullOrEmpty(BootstrapToken) ? null : BootstrapToken), ct);
            if (outcome.Value is not null)
                return LocalRedirect(SafeReturnUrl);
            Error = outcome.Message;
        }
        catch (ValidationException)
        {
            Error = "Enter your email and password.";
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        MicrosoftSignIn = (await mediator.Send(new EntraStatusQuery(), ct)).Value!.Enabled;
        PasswordResetByEmail = (await mediator.Send(new IdentityStatusQuery(), ct)).Value!.PasswordResetByEmail;
    }

    // Only same-site paths: an absolute ReturnUrl would make this page an open redirect.
    private string SafeReturnUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";
}
