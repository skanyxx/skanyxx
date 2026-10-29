using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class InstallCiValidator : AbstractValidator<InstallCiCommand>
{
    public InstallCiValidator() => RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
}
