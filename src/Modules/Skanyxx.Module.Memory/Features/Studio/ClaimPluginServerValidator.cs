using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class ClaimPluginServerValidator : AbstractValidator<ClaimPluginServerCommand>
{
    public ClaimPluginServerValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Server).Must(PluginServerRules.IsServer).WithMessage("Invalid server name.");
        RuleFor(c => c.Plugin).Must(PluginServerRules.IsPlugin).WithMessage("Invalid plugin name.");
    }
}
