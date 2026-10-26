using FluentValidation;
using FluentValidation.Results;
using Skanyxx.Module.Agents.Plugins.Sources;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>A refused source is the request's fault (400, through the validation problem); an unreachable git host is not (502).</summary>
internal static class PluginSourceFailure
{
    public static ValidationException Refused(PluginSourceException ex) => new([new ValidationFailure("source", ex.Message)]);
}
