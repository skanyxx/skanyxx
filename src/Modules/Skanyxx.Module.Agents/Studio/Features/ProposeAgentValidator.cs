using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// The form's values (<see cref="DraftProblems"/>): the same rules the files are held to at merge, plus the cloud tools
/// licence and credentials in its text (D361, D364). A server that is not on <see cref="StudioOptions.McpServers"/> may
/// still be a plugin's server in main (P7): only then is main read.
/// </summary>
internal sealed class ProposeAgentValidator : AbstractValidator<ProposeAgentCommand>
{
    public ProposeAgentValidator(DraftProblems problems)
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Draft).Cascade(CascadeMode.Stop).NotNull()
            .Must(d => d.Skills is not null && d.McpTools is not null && d.Grants is not null).WithMessage("Lists must not be null.")
            .CustomAsync(async (draft, context, ct) =>
            {
                foreach (var problem in await problems.CheckAsync(draft, ct))
                    context.AddFailure("draft", problem);
            })
            // Someone who may not propose gets the handler's 403, whatever the form holds.
            .When(c => c.User is not null && c.MayPropose);
    }
}
