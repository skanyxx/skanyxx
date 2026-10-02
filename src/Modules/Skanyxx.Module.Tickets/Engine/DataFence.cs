using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Wraps text that came from a ticket or an agent in <c>&lt;data-NONCE&gt;</c> tags with a random nonce, so the model
/// reads it as material, not instructions. Any <c>&lt;data-</c> / <c>&lt;/data-</c> inside the text is rewritten
/// (<c>data_</c>, any case), so fenced text can neither close this fence nor open one, whatever nonce it guesses.
/// </summary>
public sealed partial class DataFence
{
    private readonly string _nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    public string Tag => $"data-{_nonce}";

    public string Wrap(string text) => $"<{Tag}>\n{Tags().Replace(text, "<$1data_")}\n</{Tag}>";

    [GeneratedRegex(@"<(/?)\s*data-", RegexOptions.IgnoreCase)]
    private static partial Regex Tags();
}
