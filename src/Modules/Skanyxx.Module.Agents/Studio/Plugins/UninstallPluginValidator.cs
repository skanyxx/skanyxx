using FluentValidation;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;

namespace Skanyxx.Module.Agents.Studio.Plugins;

internal sealed class UninstallPluginValidator : AbstractValidator<UninstallPluginCommand>
{
    public UninstallPluginValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Plugin).Must(StudioNames.IsValidPluginName).WithMessage("Invalid plugin name.");
    }
}
