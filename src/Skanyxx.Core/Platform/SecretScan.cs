using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Skanyxx.Core.Platform;

/// <summary>
/// What a credential looks like in text: private keys (PEM, PGP, base64 as in a kubeconfig), well-known token formats
/// (cloud, git hosts, chat, payment, webhooks, model and package registries, Skanyxx's own <c>skx_</c> agent secrets that
/// kagent holds), a JWT, a bearer or basic credential, a URL with a password in it, and any field named like one
/// (<c>password: …</c>, <c>Pwd=…</c>, <c>signing_key=…</c>, <c>SharedAccessKey=…</c>, <c>sig=…</c>). Used to refuse it
/// where it must never be stored — a plugin PR (P3), an agent proposal, a memory card (D391) — and to blank it where text
/// from kagent is logged (<see cref="Redact"/>). Text is NFKC-normalised and stripped of invisible characters (format
/// characters, Unicode tags, combining marks, fillers) first, so a full-width letter or a zero-width space does not hide
/// a token (D391). Every pattern runs on the non-backtracking engine: time is linear in the input, whatever an
/// agent or a ticket writes.
/// </summary>
public static class SecretScan
{
    public const string Redacted = "[redacted]";

    /// <summary><see cref="Redact"/> reads at most this much: it serves log lines, which callers cut shorter still.</summary>
    public const int MaxRedacted = 4_000;

    private const RegexOptions Linear = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    private static readonly Regex Patterns = new(
        @"(?<key>-----BEGIN [A-Z ]{0,40}PRIVATE KEY(?: BLOCK)?-----[A-Za-z0-9+/=\s:,]*"
        + @"|\bLS0tLS1CRUdJTiB(?:QUklWQVRFIEtF|SU0EgUFJJVkFURSBL|FQyBQUklWQVRFIEtF|EU0EgUFJJVkFURSBL|PUEVOU1NIIFBSSVZBVEUgS0VZ|FTkNSWVBURUQgUFJJVkFURSBL|QR1AgUFJJVkFURSBL)[A-Za-z0-9+/=]*)"
        + @"|(?<url>\b[A-Za-z][A-Za-z0-9+.-]{1,20}://[^/?#\s:@'""]*:(?:[^/?#\s@'""]+|[^/?#\s@'""]*[^/?#\s@'""0-9][^/?#\s@'""]*/[^?#\s@'""]*)@)"
        + @"|(?<token>\bgh[pousr]_[A-Za-z0-9]{36,}|\bgithub_pat_[A-Za-z0-9_]{22,}|\bglpat-[A-Za-z0-9_-]{20,}|\bgldt-[A-Za-z0-9_-]{20,}"
        + @"|\bsk-[A-Za-z0-9_-]{20,}|\b[rs]k_(?:live|test)_[A-Za-z0-9]{16,}"
        + @"|\bxox[abprse]-[A-Za-z0-9-]{10,}|\bxapp-[0-9]-[A-Za-z0-9-]{10,}|hooks\.slack\.com/services/T[A-Za-z0-9]{4,}/B[A-Za-z0-9]{4,}/[A-Za-z0-9]{16,}"
        + @"|\b(?:AKIA|ASIA)[0-9A-Z]{16}\b|(?i:aws_secret_access_key)\s{0,8}[:=]\s{0,8}[""']?[A-Za-z0-9/+=]{40}"
        + @"|\bAIza[0-9A-Za-z_-]{35}|\bya29\.[A-Za-z0-9_-]{20,}|\bGOCSPX-[A-Za-z0-9_-]{20,}"
        + @"|(?i:accountkey)=[A-Za-z0-9+/]{86}==|\b[A-Za-z0-9_~.-]{3}[0-9]Q~[A-Za-z0-9_~.-]{31,34}"
        + @"|\bATATT3[A-Za-z0-9_=-]{20,}|\bhf_[A-Za-z0-9]{30,}|\bnpm_[A-Za-z0-9]{36}\b|\bpypi-AgE[A-Za-z0-9_-]{50,}"
        + @"|\bSG\.[A-Za-z0-9_-]{16,32}\.[A-Za-z0-9_-]{16,64}|\bSK[0-9a-fA-F]{32}\b|\bdo[pro]_v1_[a-f0-9]{64}"
        + @"|\bwhsec_[A-Za-z0-9+/=]{20,}|\bskx_[a-z]+_[A-Za-z0-9_-]{20,})"
        + @"|(?<jwt>\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.)"
        + @"|(?<bearer>\b(?i:bearer)\s{1,8}[A-Za-z0-9._~+/=-]{20,})"
        + @"|(?<basic>(?i:authorization)[""']?\s{0,8}[:=]\s{0,8}[""']?(?i:basic)\s{1,8}[A-Za-z0-9+/=]{8,}|\b(?i:basic)\s{1,8}[A-Za-z0-9+/]{8,}={1,2})",
        Linear);

    // Where a named value starts. The name is in group "name" (none for curl's -u user:password, a credential by its
    // form); the value is read from the match's end by Value. Forms: name: value, name = value, name => value, --name value,
    // <name>value</name>, {"name": "DB_PASSWORD", "value": …} and its YAML twin (a Kubernetes env entry). The first form
    // starts at a word every credential name contains; the whole identifier, which may start before the match, is judged.
    private static readonly Regex Names = new(
        @"(?<name>(?:token|secret|pass|pwd|auth|credential|cookie|key|sig|bearer)[A-Za-z0-9_-]{0,64})\\?[""']?[ \t]*(?:=>?|:)[ \t]*"
        + @"|(?:^|\s)(?:-u|--user)[ \t]+[""']?[^\s:'""]+:"
        + @"|(?:^|\s)--(?<name>[A-Za-z][A-Za-z0-9-]{0,40})[ \t]+"
        + @"|<(?<name>[A-Za-z][A-Za-z0-9_.-]{0,64})>"
        + @"|""name""[ \t]*:[ \t]*""(?<name>[^""\r\n]{1,64})""[ \t]*,[ \t]*""value""[ \t]*:[ \t]*"
        + @"|\bname:[ \t]*[""']?(?<name>[A-Za-z_][A-Za-z0-9_.-]{0,64})[""']?[ \t]*\r?\n[ \t]*value:[ \t]*",
        Linear | RegexOptions.IgnoreCase);

    // The value right after a credential's name, by its quoting: a double-quoted one to its closing quote, escapes
    // skipped; a single-quoted one to its quote; a JSON-in-a-JSON-string one (\") to its escaped quote; an unquoted one to
    // a comma, brace, quote, '<' or line end, a ${…} in it read whole (so ${PREFIX}Secr3t99 is judged as written). One
    // alternative per quote: the non-backtracking engine has no backreferences.
    private static readonly Regex Value = new(
        @"^(?:""(?<value>(?:[^""\\\r\n]|\\.)+)|'(?<value>[^'\r\n]+)|\\""(?<value>(?:[^""\\\r\n]|\\\\\\""|\\[^""\\\r\n])+)"
        + @"|(?<value>(?:\$\{[^}\s""'\\]*\}|[^""'\\\r\n,}\s])(?:\$\{[^}\s""'\\]*\}|[^""'\\\r\n,}<])*))",
        Linear);

    // A whole value that stands in for one or refers to one, judged after trailing ) ] ! ; , are dropped: ${TOKEN} and
    // ${A}${B} (whole, nothing after), $TOKEN (upper snake case, or letters with digits only at the end: not $ecr3tPa55),
    // $(command), %TOKEN%, %(TOKEN)s, an Ansible !vault, <your-key>, ***, {{ .Values.key }}, a call or index whose
    // argument starts with a letter or is cut by a quote (getToken(), os.environ["X"]; not Pass[1234], abc(12345); a
    // URL without user info, a relative path (~/.ssh/id_rsa, ./x), an absolute one with a second '/' or a '.'
    // (/run/secrets/x; not /Secr3t99), or the mark a redaction left (SecretText and this class write [redacted]: a card
    // memory extracted with its credentials blanked is not refused for the blank, D391).
    private static readonly Regex Reference = new(
        @"^(?:\[redacted\]?|(?:\$\{[^}\s]*\})*\$\{[^}\s]*\}?|\$(?:[A-Z_][A-Z0-9_]*|[A-Za-z_]+[0-9]*)|\$\(.*|%[A-Za-z_][A-Za-z0-9_]*%"
        + @"|%\([A-Za-z_][A-Za-z0-9_]*\)s|!vault|<[^<>\s]+>|\*+|\{\{[^}]*(?:\}\})?"
        + @"|[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)*[(\[](?:[A-Za-z_].*)?"
        + @"|(?i:https?)://[^\s@]*|(?:~|\.{1,2})/.*|/[^/]*[/.].*)$",
        Linear);

    /// <summary>What an <c>Authorization</c> value may start with before the credential (<c>Token</c>, <c>SSWS</c>, <c>ApiKey</c>): letters, shorter than this.</summary>
    private const int MaxSchemeLength = 16;

    /// <summary>
    /// A name ending in one of these, compared on its words joined in lower case (<c>DB_PASSWORD</c>, <c>apiKey</c>,
    /// <c>tokenSecret</c>, <c>x-auth-token</c>), is a credential's. A name ending in anything else is not:
    /// <c>secretName</c>, <c>token_endpoint</c>, <c>partition_key</c>, <c>public_key</c>, <c>tokenizer</c>,
    /// <c>context_tokens</c>.
    /// </summary>
    private static readonly string[] CredentialEndings =
    [
        "password", "passwd", "passphrase", "secret", "token", "refreshtokens", "accesstokens", "authtokens", "apitokens",
        "bearer", "authorization", "credential", "credentials", "cookie", "apikey", "accesskey", "secretkey", "privatekey",
        "signingkey", "encryptionkey", "masterkey", "accountkey", "clientkey", "sessionkey", "licensekey", "sharedkey", "authkey"
    ];

    /// <summary>Short credential words that count only as a name's whole last word (<c>db_pass</c>, <c>Pwd</c>, not <c>bypass</c>).</summary>
    private static readonly HashSet<string> CredentialLastWords = ["pass", "pwd", "sig"];

    /// <summary>A last word that only says how the credential is written (<c>tokenString</c>, <c>client-key-data</c>).</summary>
    private static readonly HashSet<string> EncodingWords = ["string", "str", "value", "val", "data", "raw", "hex", "b64", "base64"];

    private static readonly string[] Kinds = ["key", "url", "token", "jwt", "bearer", "basic"];

    /// <summary>How much of a named value <see cref="Find"/> reads: enough for its first word.</summary>
    private const int FoundValueLength = 256;

    /// <returns>What kind of credential the text seems to hold, or null when none.</returns>
    public static string? Find(string text)
    {
        var normalized = Normalize(text);
        if (Patterns.Match(normalized) is { Success: true } m)
            return Kinds.FirstOrDefault(k => m.Groups[k].Success) switch
            {
                "key" => "a private key",
                "url" => "a URL with a password",
                "jwt" => "a JWT",
                "bearer" => "a bearer token",
                "basic" => "a basic-auth credential",
                _ => "an API token"
            };
        return NamedValues(normalized, FoundValueLength).Any(v => LooksLikeAValue(normalized.Substring(v.Start, v.Length), v.Authorization))
            ? "a named credential (password, token, secret or key)"
            : null;
    }

    /// <summary>
    /// <paramref name="text"/> (normalised, cut to at most <see cref="MaxRedacted"/>) with every credential
    /// <see cref="Find"/> knows, and the value of every field or header named like one (<c>"apiKey": "…"</c>,
    /// <c>Authorization: …</c>, <c>password=…</c>), replaced by <see cref="Redacted"/>. Counters named like tokens
    /// (<c>total_tokens</c>) stay. The cut drops the word it lands in, so a token straddling it cannot survive as a
    /// fragment too short to be recognised (D391).
    /// </summary>
    public static string Redact(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length > MaxRedacted)
            normalized = WithoutTrailingWord(normalized[..MaxRedacted]);

        var named = new StringBuilder(normalized.Length);
        var at = 0;
        foreach (var (start, length, _) in NamedValues(normalized, normalized.Length))
        {
            if (start < at)
                continue;
            named.Append(normalized, at, start - at).Append(Redacted);
            at = start + length;
        }
        named.Append(normalized, at, normalized.Length - at);
        return Patterns.Replace(named.ToString(), Redacted);
    }

    /// <summary>
    /// <paramref name="node"/> as JSON for a log line: credentials blanked in the raw string values before they are
    /// escaped (an escaped <c>+</c> would hide a token from the patterns, D391), and every value under a property
    /// named like a credential blanked whole. Not escaped for HTML: it is never rendered as such.
    /// </summary>
    public static string RedactJson(JsonNode? node) =>
        RedactNode(node)?.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";

    /// <summary>
    /// Whether a field, header or parameter of this name holds a credential: its words (split at any other character and
    /// at a lower-to-upper case change), without a last <see cref="EncodingWords"/> one, end in a
    /// <see cref="CredentialEndings"/> word or are a <see cref="CredentialLastWords"/> one (<c>apiKey</c>, <c>db_pass</c>,
    /// <c>Pwd</c>, <c>tokenString</c>; not <c>secret_ref</c>, <c>cookie_name</c>, <c>sort_key</c>).
    /// </summary>
    public static bool IsCredentialName(string name)
    {
        var words = Parts(name).Select(w => w.ToLowerInvariant()).ToList();
        if (words.Count > 1 && EncodingWords.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);
        if (words.Count == 0)
            return false;
        var joined = string.Concat(words);
        return CredentialLastWords.Contains(words[^1]) || CredentialEndings.Any(e => joined.EndsWith(e, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether the text holds invisible characters that carry hidden text to a model ("ASCII smuggling"): a Unicode tag
    /// character (U+E0000–U+E007F), a variation selector supplement (U+E0100–U+E01EF), or a variation selector
    /// (U+FE00–U+FE0F) that does not directly follow a symbol, a keycap base or one of the four punctuation emoji
    /// (a run of selectors appended to an emoji is one byte each, OLD CR4-L3). ❤️, 1️⃣ and ™️ pass.
    /// </summary>
    public static bool HasHiddenText(string text)
    {
        Rune? previous = null;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is >= 0xE0000 and <= 0xE007F or >= 0xE0100 and <= 0xE01EF
                || (rune.Value is >= 0xFE00 and <= 0xFE0F && !(previous is { } p && IsEmojiBase(p))))
                return true;
            previous = rune;
        }
        return false;
    }

    private static bool IsEmojiBase(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.OtherSymbol or UnicodeCategory.MathSymbol
        || rune.Value is (>= '0' and <= '9') or '#' or '*' or 0x203C or 0x2049 or 0x3030 or 0x303D;

    private static JsonNode? RedactNode(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key,
            p.Value is not null && (IsCredentialName(p.Key) || (p.Key == "value" && NamesACredential(o)))
                ? JsonValue.Create(Redacted)
                : RedactNode(p.Value)))),
        JsonArray a => new JsonArray(a.Select(RedactNode).ToArray()),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => JsonValue.Create(Redact(v.GetValue<string>())),
        _ => node?.DeepClone()
    };

    /// <summary>A Kubernetes env entry or header list item: <c>{"name": "DB_PASSWORD", "value": …}</c>.</summary>
    private static bool NamesACredential(JsonObject o) =>
        o["name"] is JsonValue name && name.GetValueKind() == JsonValueKind.String && IsCredentialName(name.GetValue<string>());

    /// <summary>
    /// The value spans of every field named like a credential, in order, each read up to <paramref name="maxLength"/>
    /// characters; a value may contain later names. <see cref="Find"/> judges a value by its first word, so it reads
    /// <see cref="FoundValueLength"/>: every name inside an unquoted value runs to the line end, and reading each whole
    /// would make many names on one line quadratic. <see cref="Redact"/> reads them whole, within its cut.
    /// </summary>
    private static IEnumerable<(int Start, int Length, bool Authorization)> NamedValues(string text, int maxLength)
    {
        foreach (Match name in Names.Matches(text))
        {
            var whole = name.Groups["name"] is { Success: true } group ? WholeName(text, group) : null;
            if (whole is not null && !IsCredentialName(whole))
                continue;
            var end = name.Index + name.Length;
            if (Value.Match(text, end, Math.Min(text.Length - end, maxLength)) is { Success: true } value)
                yield return (value.Groups["value"].Index, value.Groups["value"].Length,
                    whole?.EndsWith("authorization", StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    /// <summary>The identifier the match ends: it may start earlier (<c>refresh_tokens</c> matches at <c>tokens</c>).</summary>
    private static string WholeName(string text, Group name)
    {
        var start = name.Index;
        while (start > 0 && name.Index - start < 64 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '-'))
            start--;
        return text[start..(name.Index + name.Length)];
    }

    /// <summary>A name's words: split at any character but a letter or digit and at a lower-to-upper case change (<c>dbPass</c> → db, Pass).</summary>
    private static IEnumerable<string> Parts(string name)
    {
        var start = 0;
        for (var i = 0; i <= name.Length; i++)
        {
            var split = i == name.Length || !char.IsLetterOrDigit(name[i]);
            if (!split && !(i > 0 && char.IsLower(name[i - 1]) && char.IsUpper(name[i])))
                continue;
            if (i > start)
                yield return name[start..i];
            start = split ? i + 1 : i;
        }
    }

    /// <summary>
    /// A value worth refusing a write over, judged by its first word (the second for an <c>Authorization</c> value whose
    /// first is a short scheme such as <c>Token</c>, <c>SSWS</c>, <c>ApiKey</c>) without trailing <c>) ] ! ; ,</c> (code:
    /// <c>OpenAI(api_key=API_KEY)</c>, <c>process.env.KEY!;</c>): six characters or more with a digit or a symbol other
    /// than <c>- _ .</c> (so not a name such as <c>postgres-credentials</c> or <c>db_password</c>), and not a
    /// <see cref="Reference"/>. A backtick template with a <c>${…}</c> in it is code too. Prose such as "password:
    /// required" passes.
    /// </summary>
    private static bool LooksLikeAValue(string value, bool authorization)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('`') && trimmed.Contains("${", StringComparison.Ordinal))
            return false;
        var words = trimmed.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        var first = authorization && words.Length > 1 && words[0].Length < MaxSchemeLength && words[0].All(char.IsLetter)
            ? words[1]
            : words[0];
        first = first.TrimEnd(')', ']', '!', ';', ',');
        return first.Length >= 6 && first.Any(c => !char.IsLetter(c) && c is not ('-' or '_' or '.')) && !Reference.IsMatch(first);
    }

    /// <summary>The cut text without the word the cut landed in, which may be the head of a token.</summary>
    private static string WithoutTrailingWord(string cut)
    {
        var end = cut.Length;
        while (end > 0 && (char.IsLetterOrDigit(cut[end - 1]) || cut[end - 1] is '_' or '-' or '+' or '/' or '=' or '.' or '~' or ':' or '%' or '@'))
            end--;
        return cut[..end];
    }

    /// <summary>NFKC, then without invisible characters: format (zero-width, bidi, Unicode tags), combining marks, fillers.</summary>
    private static string Normalize(string text)
    {
        string composed;
        try
        {
            composed = text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Not well-formed UTF-16 (a lone surrogate): scanned as it is, the surrogate read as U+FFFD.
            composed = text;
        }
        var builder = new StringBuilder(composed.Length);
        foreach (var rune in composed.EnumerateRunes())
            if (!IsInvisible(rune))
                builder.Append(rune);
        return builder.ToString();
    }

    private static bool IsInvisible(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
        || rune.Value is 0x115F or 0x1160 or 0x3164 or 0xFFA0;
}
