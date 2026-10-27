namespace Skanyxx.Module.ChatContext.Extraction;

/// <summary>English function words and chat filler that never describe what a conversation was about.</summary>
internal static class Stopwords
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "but", "not", "you", "your", "yours", "all", "any", "can", "could", "would", "should",
        "will", "shall", "may", "might", "must", "her", "his", "him", "she", "they", "them", "their", "theirs", "our", "ours",
        "was", "were", "been", "being", "have", "has", "had", "having", "does", "did", "doing", "done", "this", "that",
        "these", "those", "there", "here", "where", "when", "what", "which", "who", "whom", "whose", "why", "how", "with",
        "without", "within", "from", "into", "onto", "upon", "about", "above", "below", "over", "under", "again", "further",
        "then", "than", "once", "only", "own", "same", "such", "too", "very", "just", "also", "each", "few", "more", "most",
        "other", "some", "both", "either", "neither", "nor", "off", "out", "its", "it's", "itself", "myself", "yourself",
        "ourselves", "themselves", "because", "while", "until", "after", "before", "during", "through", "between", "against",
        "per", "via", "yes", "yet", "one", "two", "three", "first", "second", "new", "old", "like", "get", "got", "gets",
        "make", "makes", "made", "use", "used", "uses", "using", "need", "needs", "want", "wants", "know", "knows", "let",
        "lets", "see", "say", "says", "said", "tell", "told", "ask", "asked", "please", "thanks", "thank", "hello", "hey",
        "okay", "sure", "well", "now", "still", "even", "much", "many", "way", "thing", "things", "something", "anything",
        "everything", "nothing", "someone", "anyone", "everyone", "able", "into", "via", "etc", "lot", "lots", "really",
        "maybe", "might", "already", "always", "never", "often", "sometimes", "usually", "back", "next", "last", "take",
        "give", "going", "come", "comes", "look", "looks", "think", "thinks", "help", "helps", "answer", "question", "try",
        "don", "doesn", "didn", "isn", "aren", "wasn", "weren", "won", "wouldn", "couldn", "shouldn", "can't", "ll", "ve", "re"
    };
}
