using FluentValidation;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class EditDraftValidator : AbstractValidator<EditDraftQuery>
{
    public EditDraftValidator()
    {
        RuleFor(q => q.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(q => q.Agent).Must(StudioNames.IsValidName).WithMessage("Invalid agent name.");
    }
}
