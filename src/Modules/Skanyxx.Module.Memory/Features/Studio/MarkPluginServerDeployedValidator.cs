using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class MarkPluginServerDeployedValidator : AbstractValidator<MarkPluginServerDeployedCommand>
{
    public MarkPluginServerDeployedValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Server).Must(PluginServerRules.IsServer).WithMessage("Invalid server name.");
        RuleFor(c => c.Plugin).Must(PluginServerRules.IsPlugin).WithMessage("Invalid plugin name.");
        RuleFor(c => c.Fingerprint).NotEmpty().MaximumLength(64).Matches("^[a-f0-9]+$");
        RuleFor(c => c.SpecHash).NotEmpty().MaximumLength(64).Matches("^[a-f0-9]+$");
    }
}
