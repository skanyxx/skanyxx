using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class RecordStudioRepoValidator : AbstractValidator<RecordStudioRepoCommand>
{
    public RecordStudioRepoValidator()
    {
        RuleFor(c => c.Actor).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Repo).NotNull();
        RuleFor(c => c.Repo.Id).GreaterThan(0).When(c => c.Repo is not null);
        RuleFor(c => c.Repo.CreatedAt).NotEmpty().MaximumLength(64).When(c => c.Repo is not null);
        RuleFor(c => c.Repo.FullName).NotEmpty().MaximumLength(256).When(c => c.Repo is not null);
    }
}
