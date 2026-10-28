using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;

namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>D265: a saved model makes every kept answer stale (answers are the model's). Only registered with the cache on.</summary>
internal sealed class ModelChangeClearsAnswers(AnswerCache cache) : IPipelineBehavior<SaveModelConfigCommand, Outcome<ModelConfigDetail>>
{
    public async Task<Outcome<ModelConfigDetail>> Handle(
        SaveModelConfigCommand request, RequestHandlerDelegate<Outcome<ModelConfigDetail>> next, CancellationToken cancellationToken)
    {
        var outcome = await next();
        if (outcome.Status is OutcomeStatus.Ok or OutcomeStatus.Created)
            await cache.ClearAllAsync();
        return outcome;
    }
}
