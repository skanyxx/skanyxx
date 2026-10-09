using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Identity.Email;

/// <summary>Startup refuses unusable SMTP settings, naming what is wrong (never the password).</summary>
internal sealed class SmtpOptionsValidation : IValidateOptions<IdentityModuleOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityModuleOptions options) =>
        options.Smtp.Problem() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
