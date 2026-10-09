using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class MarkStudioSecretDeployedValidator : AbstractValidator<MarkStudioSecretDeployedCommand>
{
    public MarkStudioSecretDeployedValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.AgentId).Must(Scope.IsValidId).WithMessage("Invalid agent id.");
        RuleFor(c => c.Fingerprint).NotEmpty().MaximumLength(64).Matches("^[a-f0-9]+$");
    }
}
