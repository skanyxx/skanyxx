using FluentValidation;

namespace Skanyxx.Module.Identity.Features.Refresh;

internal sealed class RefreshValidator : AbstractValidator<RefreshCommand>
{
    public RefreshValidator() => RuleFor(c => c.RefreshToken).NotEmpty().MaximumLength(8192);
}
