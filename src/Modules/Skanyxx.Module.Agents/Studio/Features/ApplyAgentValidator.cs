using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>The form itself is judged by the proposal it opens (<see cref="ProposeAgentValidator"/>).</summary>
internal sealed class ApplyAgentValidator : AbstractValidator<ApplyAgentCommand>
{
    public ApplyAgentValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Draft).NotNull();
    }
}
