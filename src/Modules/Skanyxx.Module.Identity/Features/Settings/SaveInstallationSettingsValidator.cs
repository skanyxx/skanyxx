using FluentValidation;
using Skanyxx.Core.Platform.Settings;

namespace Skanyxx.Module.Identity.Features.Settings;

internal sealed class SaveInstallationSettingsValidator : AbstractValidator<SaveInstallationSettingsCommand>
{
    public SaveInstallationSettingsValidator()
    {
        RuleFor(c => c.ActorId).NotEmpty();
        RuleFor(c => c.Values).NotNull();
        RuleForEach(c => c.Values).Custom((pair, context) =>
        {
            if (InstallationSettings.Find(pair.Key) is not { } setting)
                context.AddFailure("values", $"Unknown setting '{pair.Key}'.");
            else if (InstallationSettings.Problem(setting, pair.Value ?? "") is { } problem)
                context.AddFailure(pair.Key, problem);
        });
    }
}
