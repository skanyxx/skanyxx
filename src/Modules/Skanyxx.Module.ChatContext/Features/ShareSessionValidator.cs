using FluentValidation;
using Skanyxx.Module.ChatContext.Access;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class ShareSessionValidator : AbstractValidator<ShareSessionCommand>
{
    public ShareSessionValidator()
    {
        RuleFor(c => c.SessionId).SessionId();
        RuleFor(c => c.Scope).Must(SessionScope.IsValid).WithMessage("Invalid scope.").OverridePropertyName("scope");
    }
}
