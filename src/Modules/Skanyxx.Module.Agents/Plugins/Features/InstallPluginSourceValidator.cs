using FluentValidation;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Features;

internal sealed class InstallPluginSourceValidator : AbstractValidator<InstallPluginSourceCommand>
{
    public InstallPluginSourceValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        // Someone who may not install from that source gets the handler's 403, whatever the request holds.
        When(c => c.User is not null && c.Request is not null && PluginAccess.CanInstall(c.User, c.Request.Source), () =>
        {
            RuleFor(c => c.Request).SetValidator(c => PluginSourceRequestRules.For(c.Upload is not null));
            RuleFor(c => c.Request.Digest).NotNull().WithMessage("digest is required: install what inspect showed.");
        });
    }
}
