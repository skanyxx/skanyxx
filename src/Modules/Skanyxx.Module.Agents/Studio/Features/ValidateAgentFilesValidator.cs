using FluentValidation;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class ValidateAgentFilesValidator : AbstractValidator<ValidateAgentFilesQuery>
{
    public ValidateAgentFilesValidator()
    {
        RuleFor(q => q.Name).NotEmpty().MaximumLength(63).WithMessage("name: the agent's folder name.");
        RuleFor(q => q.AgentYaml).NotNull().MaximumLength(IAgentRepo.MaxFileBytes).WithMessage($"agentYaml: required, at most {IAgentRepo.MaxFileBytes / 1024} KB.");
        RuleFor(q => q.GrantsYaml).NotNull().MaximumLength(IAgentRepo.MaxFileBytes).WithMessage($"grantsYaml: required, at most {IAgentRepo.MaxFileBytes / 1024} KB.");
    }
}
