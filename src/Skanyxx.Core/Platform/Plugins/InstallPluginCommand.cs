using MediatR;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// Opens one studio pull request with the plugin's files (<c>plugins/&lt;name&gt;/plugin.yaml</c>, its
/// <c>mcp/&lt;server&gt;.yaml</c> and its agents' folders); a reinstall also removes what the new version dropped. Nothing
/// reaches kagent: a supervisor's merge does. Literal header values are stored for the reconciler, never written to git.
/// A plugin from git or a zip is the owner's to install; one from the built-in catalog a builder's too.
/// </summary>
public sealed record InstallPluginCommand(StudioUser User, PluginDraft Draft) : IRequest<Outcome<StudioResult>>;
