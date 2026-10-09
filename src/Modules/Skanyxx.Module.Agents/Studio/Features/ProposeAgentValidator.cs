using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>The form's values (<see cref="DraftRules"/>): the same rules the files are held to at merge.</summary>
internal sealed class ProposeAgentValidator : AbstractValidator<ProposeAgentCommand>
{
    public ProposeAgentValidator(IOptions<StudioOptions> options)
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Draft).Cascade(CascadeMode.Stop).NotNull()
            .Must(d => d.Skills is not null && d.McpTools is not null && d.Grants is not null).WithMessage("Lists must not be null.")
            .Custom((draft, context) =>
            {
                foreach (var problem in DraftRules.Check(draft, options.Value))
                    context.AddFailure("draft", problem);
            })
            // Someone who may not propose gets the handler's 403, whatever the form holds.
            .When(c => c.User is { CanPropose: true });
    }
}
