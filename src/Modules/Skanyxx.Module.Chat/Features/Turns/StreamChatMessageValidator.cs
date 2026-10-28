using FluentValidation;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.Chat.Features.Turns;

internal sealed class StreamChatMessageValidator : AbstractValidator<StreamChatMessageCommand>
{
    public StreamChatMessageValidator(IValidator<SendChatMessageCommand> turn) => RuleFor(c => c.Turn).SetValidator(turn);
}
