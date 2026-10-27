using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.ChatContext.Extraction;

/// <summary>
/// The default extractor (D239), no model call: keywords are non-stop words weighted by how often they occur (the
/// person's words count three times the agent's), categories are the configured stems those keywords hit, ticket keys
/// are Jira-shaped words, and the outcome is the opening of the latest answer.
/// </summary>
internal sealed partial class DeterministicExtractor(IOptions<ChatContextOptions> options) : IChatExtractor
{
    private const int MessageWeight = 3;
    private const int MaxReplyChars = 4_000;
    private const int OutcomeSentences = 2;

    // Key-shaped words that are standards or versions, not tickets.
    private static readonly HashSet<string> NotProjects =
        ["UTF", "SHA", "ISO", "RFC", "HTTP", "TLS", "SSL", "MD", "CVE", "COVID", "IPV", "ES", "PEP", "AES", "RSA", "X", "GPT", "WCAG"];

    private readonly IReadOnlyDictionary<string, string[]> _categories =
        options.Value.Categories.Count > 0 ? options.Value.Categories : DefaultCategories.All;

    private readonly HashSet<string> _projects = [.. options.Value.TicketProjects];

    public Task<SessionDigest> ExtractAsync(SessionDigest current, ChatTurn turn, CancellationToken ct)
    {
        var reply = turn.Reply.Length > MaxReplyChars ? turn.Reply[..MaxReplyChars] : turn.Reply;
        var weights = new Dictionary<string, int>(current.KeywordWeights);
        foreach (var word in Words(turn.Message))
            weights[word] = weights.GetValueOrDefault(word) + MessageWeight;
        foreach (var word in Words(reply))
            weights[word] = weights.GetValueOrDefault(word) + 1;
        var kept = weights.OrderByDescending(w => w.Value).ThenBy(w => w.Key, StringComparer.Ordinal)
            .Take(SessionDigest.MaxWeights).ToDictionary();

        var tickets = current.TicketKeys
            .Concat(TicketKeys(turn.Message)).Concat(TicketKeys(reply))
            .Distinct().Take(ChatTickets.MaxPerSession).ToList();
        var outcome = Outcome(reply);

        return Task.FromResult(new SessionDigest(kept, Categories(kept), tickets, outcome.Length > 0 ? outcome : current.Outcome));
    }

    /// <summary>
    /// Lowercased words worth indexing, in text order, repeats kept (they are the weight). Ticket keys are not words: their
    /// project (<c>sdb</c> of SDB-58) would make every chat of that Jira project look related. Standards shaped like keys
    /// (UTF-8, SHA-256) stay words.
    /// </summary>
    internal static IEnumerable<string> Words(string text) =>
        AllWords(TicketPattern().Replace(text, m => IsProjectShaped(m.Value) ? " " : m.Value));

    /// <summary>Every lowercased non-stop word, ticket keys' parts included: what a person types into a search.</summary>
    internal static IEnumerable<string> AllWords(string text) =>
        WordPattern().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length is >= 3 and <= 40 && w.Any(char.IsLetter) && !Stopwords.All.Contains(w));

    private List<string> Categories(IReadOnlyDictionary<string, int> weights) =>
        [.. _categories
            .Select(c => (Name: c.Key, Score: weights.Where(w => c.Value.Any(stem => w.Key.StartsWith(stem, StringComparison.Ordinal))).Sum(w => w.Value)))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score).ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(SessionDigest.MaxCategories)
            .Select(c => c.Name)];

    private IEnumerable<string> TicketKeys(string text) =>
        TicketPattern().Matches(text)
            .Select(m => m.Value)
            .Where(key => ChatTickets.IsValid(key) && IsProject(key[..key.IndexOf('-')]));

    private bool IsProject(string project) =>
        _projects.Count > 0 ? _projects.Contains(project) : project.Length >= 2 && !NotProjects.Contains(project);

    private static bool IsProjectShaped(string key) => key.IndexOf('-') is var dash && dash >= 2 && !NotProjects.Contains(key[..dash]);

    /// <summary>The first sentences of the answer as plain text: code blocks and markdown marks dropped, whitespace folded.</summary>
    internal static string Outcome(string reply)
    {
        var text = WhitespacePattern().Replace(MarkdownPattern().Replace(CodeBlockPattern().Replace(reply, " "), ""), " ").Trim();
        var sentences = SentencePattern().Matches(text).Select(m => m.Value.Trim()).Where(s => s.Length > 0).Take(OutcomeSentences);
        var outcome = string.Join(' ', sentences);
        if (outcome.Length == 0)
            outcome = text;
        return TextCut.Cut(outcome, Domain.ChatSession.MaxOutcome);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:'[\p{L}]+)?")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\b[A-Z][A-Z0-9_]{0,29}-[1-9][0-9]{0,9}\b")]
    private static partial Regex TicketPattern();

    [GeneratedRegex(@"```.*?(```|\z)", RegexOptions.Singleline)]
    private static partial Regex CodeBlockPattern();

    [GeneratedRegex(@"[#*_`>|]+")]
    private static partial Regex MarkdownPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[^.!?]+(?:[.!?]+|\z)")]
    private static partial Regex SentencePattern();
}
