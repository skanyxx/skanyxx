using FluentValidation;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class GetSessionValidator : AbstractValidator<GetSessionQuery>
{
    public GetSessionValidator()
    {
        RuleFor(q => q.SessionId).SessionId();
    }
}
