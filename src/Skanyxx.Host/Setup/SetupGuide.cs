namespace Skanyxx.Host.Setup;

/// <summary>
/// How to make one feature work: what it does, what it needs, the steps for a local run and for the Helm chart, how
/// to tell it works and what usually goes wrong. Text only: a guide names settings, never their values.
/// </summary>
/// <param name="Id">Stable, lowercase; the page and <c>/api/setup/guides/{id}</c> use it.</param>
/// <param name="Readme">The README section the facts come from.</param>
public sealed record SetupGuide(
    string Id,
    string Title,
    string Readme,
    string What,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<SetupSetting> Settings,
    IReadOnlyList<SetupStep> Local,
    IReadOnlyList<SetupStep> Helm,
    IReadOnlyList<SetupStep> Verify,
    IReadOnlyList<SetupError> Errors);
