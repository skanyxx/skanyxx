using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// The form's values (<see cref="DraftRules"/>): the same rules the files are held to at merge. A server that is not on
/// <see cref="StudioOptions.McpServers"/> may still be a plugin's server in main (P7): only then is main read.
/// </summary>
internal sealed class ProposeAgentValidator : AbstractValidator<ProposeAgentCommand>
{
    public ProposeAgentValidator(IOptions<StudioOptions> options, StudioReconciler reconciler)
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Draft).Cascade(CascadeMode.Stop).NotNull()
            .Must(d => d.Skills is not null && d.McpTools is not null && d.Grants is not null).WithMessage("Lists must not be null.")
            .CustomAsync(async (draft, context, ct) =>
            {
                IReadOnlyDictionary<string, string> pluginServers = new Dictionary<string, string>();
                if (options.Value.Configured && draft.McpTools.Any(t => !options.Value.McpServers.Contains(t?.Server ?? "")))
                {
                    try
                    {
                        pluginServers = await reconciler.MainPluginServersAsync(ct);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
                    {
                        context.AddFailure("draft", "The plugin servers in main cannot be read right now; try again in a moment.");
                    }
                }
                foreach (var problem in DraftRules.Check(draft, options.Value, pluginServers))
                    context.AddFailure("draft", problem);
            })
            // Someone who may not propose gets the handler's 403, whatever the form holds.
            .When(c => c.User is { CanPropose: true });
    }
}
