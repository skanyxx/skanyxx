using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Grants;

internal sealed class SetAgentGrantsValidator : AbstractValidator<SetAgentGrantsCommand>
{
    private const int MaxGrants = 50;

    public SetAgentGrantsValidator()
    {
        RuleFor(c => c.Caller).SetValidator(new CallerValidator());
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
        RuleFor(c => c.Grants).Cascade(CascadeMode.Stop).NotNull()
            .NotEmpty().WithMessage("To revoke an agent, send one grant with canSearch and canUpsert false; an empty list would mean the default.")
            .Must(g => g.All(e => e is not null)).WithMessage("Grants must not contain null entries.")
            .Must(g => g.Count <= MaxGrants).WithMessage($"At most {MaxGrants} grants per agent.")
            .Must(g => g.Select(e => e.Scope).Distinct().Count() == g.Count).WithMessage("Each scope may appear once.");
        RuleForEach(c => c.Grants).ChildRules(entry =>
        {
            entry.RuleFor(e => e.Scope)
                .Must(s => s == AgentGrant.CallerPersonal || (Scope.TryParse(s, out var scope) && scope.Level != ScopeLevel.Personal))
                .WithMessage("Scope must be 'personal' (the caller's own), 'company', or 'team|department:<id>'.");
        });
    }
}
