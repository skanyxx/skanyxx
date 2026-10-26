using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// A plugin draft is untrusted whatever translated it: every rule the merged files are held to (<see cref="PluginRules"/>,
/// <see cref="DraftRules"/> with the plugin's own servers), plus what only an install carries (a literal header value).
/// </summary>
internal sealed class InstallPluginValidator : AbstractValidator<InstallPluginCommand>
{
    public InstallPluginValidator(IOptions<StudioOptions> options, IOptions<PluginsOptions> plugins)
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Draft).Cascade(CascadeMode.Stop).NotNull()
            .Must(d => d.Name is not null && d.Description is not null && d.Source is not null && d.Agents is not null && d.Servers is not null && d.Notes is not null
                && d.Agents.All(a => a is { Skills: not null, McpTools: not null, Grants: not null })
                && d.Servers.All(s => s is { Headers: not null } && s.Headers.All(h => h is not null))
                && d.Notes.All(n => n is not null))
            .WithMessage("Every part of the plugin is required.")
            .Custom((draft, context) =>
            {
                var servers = draft.Servers.Select(s => s.Name).ToList();
                var problems = PluginRules.CheckPlugin(InstalledPlugin.From(draft))
                    .Concat(draft.Servers.SelectMany(s => PluginRules.CheckServer(s, options.Value, plugins.Value).Concat(PluginRules.CheckHeaderValues(s))))
                    .Concat(draft.Agents.SelectMany(a => DraftRules.Check(a, options.Value, servers.ToDictionary(s => s, _ => draft.Name), servers).Concat(PluginRules.CheckRecipe(a, servers))));
                foreach (var problem in problems)
                    context.AddFailure("draft", problem);
            })
            // Someone who may not install gets the handler's 403, whatever the plugin holds.
            .When(c => c.User is { CanPropose: true });
    }
}
