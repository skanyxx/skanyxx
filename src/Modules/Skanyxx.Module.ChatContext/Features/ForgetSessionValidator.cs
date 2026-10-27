using FluentValidation;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class ForgetSessionValidator : AbstractValidator<ForgetSessionCommand>
{
    public ForgetSessionValidator()
    {
        RuleFor(c => c.SessionId).SessionId();
    }
}
