namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>
/// The repo guard refuses (D119): the agent repo is gone, a different one, public, or main is not protected as the
/// studio requires. Nothing changes until a person acts; the guard has already logged it at Error.
/// </summary>
internal sealed class StudioRepoException(string message) : StudioApplyException(message);
