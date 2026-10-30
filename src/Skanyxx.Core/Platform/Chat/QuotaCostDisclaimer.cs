namespace Skanyxx.Core.Platform.Chat;

/// <summary>Shown, large, wherever the quotas show a cost (D336): the page and every API answer that carries one.</summary>
public static class QuotaCostDisclaimer
{
    public const string Title = "This is an estimate, not a bill.";

    public const string Text =
        "Costs here are informational only and not accurate: they are worked out from the per-model prices set in configuration " +
        "(per million input and output tokens) and from the model's token counts, which can include Chat's own estimates. " +
        "Your provider's pricing may differ, and its invoice is what you actually pay.";
}
