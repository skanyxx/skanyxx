using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class RemovePluginServerValidator : AbstractValidator<RemovePluginServerCommand>
{
    public RemovePluginServerValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Server).Must(PluginServerRules.IsServer).WithMessage("Invalid server name.");
        RuleFor(c => c.Plugin).Must(p => p is null || PluginServerRules.IsPlugin(p)).WithMessage("Invalid plugin name.");
    }
}
