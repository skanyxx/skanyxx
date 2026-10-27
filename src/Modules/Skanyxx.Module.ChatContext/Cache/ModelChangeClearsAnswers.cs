using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>D241: a saved model makes every kept answer stale (answers are the model's). Only registered with the cache on.</summary>
internal sealed class ModelChangeClearsAnswers(AnswerCache cache) : IPipelineBehavior<SaveModelSettingsCommand, Outcome<ModelSettings>>
{
    public async Task<Outcome<ModelSettings>> Handle(
        SaveModelSettingsCommand request, RequestHandlerDelegate<Outcome<ModelSettings>> next, CancellationToken cancellationToken)
    {
        var outcome = await next();
        if (outcome.Status == OutcomeStatus.Ok)
            await cache.ClearAllAsync();
        return outcome;
    }
}
