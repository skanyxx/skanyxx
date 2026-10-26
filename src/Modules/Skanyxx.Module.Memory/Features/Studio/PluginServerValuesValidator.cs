using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class PluginServerValuesValidator : AbstractValidator<PluginServerValuesQuery>
{
    public PluginServerValuesValidator() => RuleFor(q => q.Server).Must(PluginServerRules.IsServer).WithMessage("Invalid server name.");
}
