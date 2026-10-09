using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class ProposalQueryValidator : AbstractValidator<ProposalQuery>
{
    public ProposalQueryValidator()
    {
        RuleFor(r => r.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(r => r.Number).InclusiveBetween(1, 999_999);
    }
}
