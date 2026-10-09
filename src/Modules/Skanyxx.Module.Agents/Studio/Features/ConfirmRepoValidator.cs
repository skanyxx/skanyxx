using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class ConfirmRepoValidator : AbstractValidator<ConfirmRepoCommand>
{
    public ConfirmRepoValidator() => RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
}
