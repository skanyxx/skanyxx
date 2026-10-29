namespace Skanyxx.Core.Platform.Chat;

/// <summary>Shown, large, wherever the quotas show a cost (D336): the page and every API answer that carries one.</summary>
public static class QuotaCostDisclaimer
{
    public const string Title = "This is an estimate, not a bill.";

    public const string Text =
        "Costs here are worked out from prices the owner typed in, per 1,000 tokens, and from token counts that can include Chat's own " +
        "estimates. Your provider's pricing may differ, and its invoice is what you actually pay.";
}
