namespace Skanyxx.Host.Tests;

/// <summary>
/// ui.js's declarative hooks in a real browser: Cancel or Escape on a confirm sends nothing, a confirmed or data-once form
/// goes once (and again after a back/forward restore), and skx.ask answers only for its submit button, also after an
/// earlier save (the Audit "Save view" bug). Ui/ui-behaviour.html runs the real ui.js in headless Chrome
/// (<see cref="HeadlessChrome"/>). Skipped when this machine has no Chrome or Chromium.
/// </summary>
public sealed class UiBehaviourTests
{
    private static readonly string[] Checks =
    [
        "confirmOpens", "dangerFocusesCancel", "cancelSendsNothing", "escapeSendsNothing", "okSendsOnce",
        "confirmedDoesNotSendTwice", "dataOnceSendsOnce", "restoredPageCanSendAgain", "askSave", "askCancelAfterASave",
        "askEscapeAfterASave"
    ];

    [Fact]
    public async Task TheDeclarativeHooks_ActOnlyWhenThePersonMeansIt()
    {
        var chrome = HeadlessChrome.Find();
        if (chrome is null)
            Assert.Skip("No Chrome or Chromium on this machine (set CHROME_PATH to run the ui.js behaviour checks).");

        var uiJs = new Uri(Repo.Path("src", "Skanyxx.Host", "wwwroot", "js", "ui.js")).AbsoluteUri;
        var html = (await File.ReadAllTextAsync(Repo.Path("tests", "Skanyxx.Host.Tests", "Ui", "ui-behaviour.html"), TestContext.Current.CancellationToken))
            .Replace("{{UI_JS}}", uiJs, StringComparison.Ordinal);

        var result = await HeadlessChrome.RunAsync(chrome!, html, "uiBehaviour");

        Assert.False(result.TryGetProperty("error", out var error), error.ToString());
        Assert.All(Checks, check => Assert.True(result.GetProperty(check).GetBoolean(), check));
    }
}
