namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// An investigation run succeeded (D343): kept by memory as one card in the starter's personal scope, pointing back at
/// the run (<c>investigation:&lt;id&gt;</c>). <paramref name="Finding"/> is the last step's answer, cut; never the whole run.
/// </summary>
public sealed record InvestigationFinished(Guid RunId, string OwnerUserId, string WorkflowName, string Input, string Finding);
