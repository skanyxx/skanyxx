using MediatR;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>
/// D241: a written card may change answers. A personal card only its owner's (their generation); any shared one —
/// company, team, department — possibly anyone's (the epoch).
/// </summary>
internal sealed class CardWritesClearAnswers(AnswerCache cache) : INotificationHandler<CardsChanged>
{
    public Task Handle(CardsChanged notification, CancellationToken cancellationToken)
    {
        if (!cache.On)
            return Task.CompletedTask;
        const string personal = "personal:";
        return notification.Scope.StartsWith(personal, StringComparison.Ordinal)
            ? cache.ClearAsync(notification.Scope[personal.Length..])
            : cache.ClearAllAsync();
    }
}
