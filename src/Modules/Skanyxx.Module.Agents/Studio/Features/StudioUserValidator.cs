using FluentValidation;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>Shared child rule: a signed-in person (FluentValidation skips a null child, so the parent checks NotNull).</summary>
internal sealed class StudioUserValidator : AbstractValidator<StudioUser>
{
    public StudioUserValidator() =>
        RuleFor(u => u.UserId).Must(Identifier.IsValid).WithMessage("A signed-in user is required.");
}
