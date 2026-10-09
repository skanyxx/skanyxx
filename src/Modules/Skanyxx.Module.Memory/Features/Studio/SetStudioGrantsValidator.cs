using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class SetStudioGrantsValidator : AbstractValidator<SetStudioGrantsCommand>
{
    public SetStudioGrantsValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
        RuleFor(c => c.Grants).Cascade(CascadeMode.Stop).NotNull()
            .Must(g => g.All(e => e is not null)).WithMessage("Grants must not contain null entries.")
            .Must(g => g.Count <= StudioGrantRules.MaxGrants).WithMessage($"At most {StudioGrantRules.MaxGrants} grants per agent.")
            .Must(g => g.Select(e => e.Scope).Distinct().Count() == g.Count).WithMessage("Each scope may appear once.");
        RuleForEach(c => c.Grants).ChildRules(entry => entry.RuleFor(e => e.Scope)
            .Must(StudioGrantRules.IsSharedScope).WithMessage("Scope must be 'company' or 'team|department:<id>'."));
    }
}
