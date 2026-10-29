using System.Diagnostics;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D391 (security review M3/L2, code review M1/L7): the formats the first scanner missed, named values in
/// <see cref="SecretScan.Find"/>, normalisation against zero-width and full-width disguises, whole quoted values, counters
/// left alone, and time linear in the input.
/// </summary>
public sealed class SecretScanTests
{
    [Theory]
    [InlineData("db password: Sup3rS3cret!")]
    [InlineData("postgres://app:Sup3rS3cret@db/app")]
    [InlineData("Authorization: Basic dXNlcjpwYXNzd29yZA")]
    [InlineData("curl -H 'basic dXNlcjpwYXNzd29yZA=='")]
    [InlineData("BEARER abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("sk_" + "live_abcdefghijklmnopqrstuvwx")]
    [InlineData("rk_" + "live_abcdefghijklmnopqrstuvwx")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=x;AccountKey" + "=abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz01234567==")]
    [InlineData("client secret abc8Q~abcdefghijklmnopqrstuvwxyz0123456")]
    [InlineData("ATA" + "TT3xFfGF0abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("hf" + "_abcdefghijklmnopqrstuvwxyzABCDEFGH")]
    [InlineData("npm" + "_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("pypi-" + "AgEIcHlwaS5vcmcCJGFiY2RlZmdoLWlqa2wtbW5vcC1xcnN0LXV2d3h5ejAxMjM0")]
    [InlineData("xapp" + "-1-A0123456789-0123456789012-abcdef")]
    [InlineData("https://hooks.slack.com/" + "services/T0123ABCD/B0123ABCD/abcdefghijklmnopqrstuvwx")]
    [InlineData("SG" + ".abcdefghijklmnopqrstuv.abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG")]
    [InlineData("SK" + "0123456789abcdef0123456789abcdef")]
    [InlineData("dop_" + "v1_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("gl" + "dt-abcdefghijklmnopqrstuvwx")]
    [InlineData("ya29" + ".a0AfH6SMBabcdefghijklmnopqrstuvwxyz")]
    [InlineData("gh" + "p_0123456789abcdefghij​klmnopqrstuvwxyz AB")]
    [InlineData("gh​p_0123456789abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("ｇｈｐ_0123456789abcdefghijklmnopqrstuvwxyzAB")]
    public void TheFormatsTheFirstScannerMissed_AreFound(string text) => Assert.NotNull(SecretScan.Find(text));

    /// <summary>D391 (security review round 2, N2 and N12; code review L-A and L-B): each one was missed.</summary>
    [Theory]
    [InlineData("password: P$ssw0rd!")]
    [InlineData("password: hunter2*")]
    [InlineData("db password: Tr0ub$dor99")]
    [InlineData("token: abc$123456789")]
    [InlineData("tokenSecret: Summer2024!")]
    [InlineData("tokenString=AbC123xyz789")]
    [InlineData("tokens_password=Summer2024!")]
    [InlineData("refresh_tokens: abcdef123456")]
    [InlineData("tokens_secret: Abc123xyz9")]
    [InlineData("tokensecret=Abc123xyz9")]
    [InlineData("pwd=Tr0ub4dor3")]
    [InlineData("Driver={ODBC};Server=db;Uid=app;Pwd=Tr0ub4dor3;")]
    [InlineData("pass: Summer2024!")]
    [InlineData("encryption_key=0123456789abcdef")]
    [InlineData("SIGNING_KEY=AbC123xyz789")]
    [InlineData("master_key: AbC123xyz789")]
    [InlineData("    client-key-data: LS0tLS1CRUdJTiBSU0EgUFJJVkFURSBLRVktLS0tLQpNSUlFb3dJQkFBS0NBUUVB")]
    [InlineData("key: LS0tLS1CRUdJTiBQUklWQVRFIEtFWS0tLS0tCk1JSUV2Z0lCQURBTkJna3Foa2lHOXcw")]
    [InlineData("redis://:Sup3rS3cret@cache:6379/0")]
    [InlineData("postgres://app:Sup3r/S3cret@db/app")]
    [InlineData("Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=AbC123xyz789+/=")]
    [InlineData("https://acct.blob.core.windows.net/c?sv=2022-11-02&sp=r&sig=AbC123xyz789%2Bdef%3D")]
    [InlineData("-----" + "BEGIN PGP PRIVATE KEY BLOCK-----")]
    [InlineData("GOCSPX-abcdefghijklmnopqrstuvwx")]
    [InlineData("whsec" + "_abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("xox" + "e-1-abcdefghijklmnop")]
    [InlineData("\"password\": \"it's-Secr3t99\"")]
    [InlineData("\"password\": \"ab\\\"cd-Secr3t99\"")]
    [InlineData("gh\U000E0041p_0123456789abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("gh\u034Fp_0123456789abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("gh\uFE0Fp_0123456789abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("gh\u3164p_0123456789abcdefghijklmnopqrstuvwxyzAB")]
    public void TheFormatsTheSecondReviewFound_AreFound(string text) => Assert.NotNull(SecretScan.Find(text));

    [Theory]
    [InlineData("password: required, at least 12 characters")]
    [InlineData("Use Bearer ${GITHUB_TOKEN} in scripts.")]
    [InlineData("api_key: <your-key>")]
    [InlineData("\"total_tokens\": 12345")]
    [InlineData("Basic configuration lives in the README.")]
    [InlineData("https://example.com/docs")]
    [InlineData("password: ${DB_PASSWORD}")]
    [InlineData("password: $DB_PASSWORD")]
    [InlineData("password: ********")]
    [InlineData("token: {{ .Values.token }}")]
    [InlineData("monkey: Banana123, signal: Strong42, compass: North99")]
    [InlineData("bypass: route-42a, passenger: seat-12A")]
    [InlineData("sort_order: created_at")]
    [InlineData("{\"tokens\": 123456, \"completion_tokens\": 4096, \"maxTokens\": 819200}")]
    public void OrdinaryText_IsNot(string text) => Assert.Null(SecretScan.Find(text));

    /// <summary>
    /// D391 (code review round 3, CR-M1): ordinary engineering text the round-2 scanner refused, every example the review
    /// measured. A name is a credential's only when it ends in one (<c>secretName</c>, <c>token_endpoint</c>,
    /// <c>partition_key</c> do not), and a value that is a name, a reference or code is not one.
    /// </summary>
    [Theory]
    [InlineData("partition_key: tenant_id")]
    [InlineData("primary_key: user_id")]
    [InlineData("sort_key: created_at")]
    [InlineData("routing_key: orders.created")]
    [InlineData("idempotency_key: order-id")]
    [InlineData("public_key: ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl ana@laptop")]
    [InlineData("ssh_key: ~/.ssh/id_rsa")]
    [InlineData("tokenizer: cl100k_base")]
    [InlineData("token_endpoint: https://login.example.com/oauth2/v2.0/token")]
    [InlineData("tokenUrl: https://login.example.com/oauth2/token")]
    [InlineData("token_ttl: 3600000")]
    [InlineData("context_tokens: 128000")]
    [InlineData("budget_tokens: 16000")]
    [InlineData("secretName: postgres-credentials")]
    [InlineData("secret_ref: vault/prod/db")]
    [InlineData("cookie_name: session_id")]
    [InlineData("passwordPolicy: minLength=12")]
    [InlineData("password: string;")]
    [InlineData("  password?: string;")]
    [InlineData("const apiKey: process.env.API_KEY")]
    [InlineData("const apiKey = process.env.OPENAI_API_KEY;")]
    [InlineData("token = getToken();")]
    [InlineData("api_key = os.environ[\"OPENAI_API_KEY\"]")]
    [InlineData("password = os.getenv(\"DB_PASSWORD\")")]
    [InlineData("token: ${{ secrets.GITHUB_TOKEN }}")]
    [InlineData("password: $(cat /run/secrets/db)")]
    [InlineData("password_file: /run/secrets/db_password")]
    [InlineData("secret: /var/run/secrets/kubernetes.io/serviceaccount/token")]
    [InlineData("set DB_PASSWORD=%DB_PASSWORD%")]
    [InlineData("https://registry.npmjs.org:443/@scope/pkg")]
    [InlineData("docker://registry:5000/app@sha256:4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945")]
    [InlineData("http://localhost:3000/users/@me")]
    [InlineData("https://host:8080/a?email=a@b.com")]
    [InlineData("https://mcp.example.com:8443/@org/x")]
    [InlineData("existingSecret: my-release-postgresql")]
    [InlineData("--password-stdin < ~/.docker/token")]
    [InlineData("docker run -u 1000:1000 app")]
    [InlineData("the <password> field is required; use <token> from the vault")]
    public void EngineeringText_IsNot(string text) => Assert.Null(SecretScan.Find(text));

    /// <summary>
    /// D391 (round 4, CR4-L1): code that names a credential without holding one, refused until trailing <c>) ] ! ; ,</c>
    /// were dropped and backtick templates, <c>%(x)s</c> and Ansible's <c>!vault</c> read as references.
    /// </summary>
    [Theory]
    [InlineData("conn = psycopg.connect(host=h, user=user, password=db_password)")]
    [InlineData("client = OpenAI(api_key=API_KEY)")]
    [InlineData("login(user, password=password)")]
    [InlineData("client_secret=client_secret_value)")]
    [InlineData("const apiKey = process.env.STRIPE_KEY!;")]
    [InlineData("headers: { Authorization: `Bearer ${token}` },")]
    [InlineData("password: \"%(DB_PASSWORD)s\"")]
    [InlineData("password: !vault |")]
    [InlineData("api_key = config[\"openai\"][\"key\"]")]
    [InlineData("token = get_token(scope)")]
    [InlineData("password: ${DB_PASSWORD:-changeme}")]
    [InlineData("password: ${DB_USER}${DB_SUFFIX}")]
    [InlineData("Authorization: Bearer ${token}")]
    [InlineData("Authorization: required on every admin route")]
    [InlineData("Authorization: Token <your-token>")]
    public void EngineeringTextRoundFour_IsNot(string text) => Assert.Null(SecretScan.Find(text));

    /// <summary>
    /// D391 (S4-L1, S4-L2): an Authorization value is judged past its scheme, and a reference must have a reference's
    /// shape, not merely its first character.
    /// </summary>
    [Theory]
    [InlineData("Authorization: Token 1234567890abcdef")]
    [InlineData("Authorization: ApiKey AbC123xyz789")]
    [InlineData("Authorization: SSWS 00Ab1C2d3E4f5G6h7I8j")]
    [InlineData("Authorization: Splunk 12345678-1234-1234-1234-123456789012")]
    [InlineData("Proxy-Authorization: Digest AbC123xyz789")]
    [InlineData("X-Custom-Authorization: Key AbC123xyz789")]
    [InlineData("\"Authorization\": \"Token 1234567890abcdef\"")]
    [InlineData("password: $ecr3tPa55")]
    [InlineData("password: /Secr3t99")]
    [InlineData("password: Pass[1234]")]
    [InlineData("password: abc(12345")]
    [InlineData("password: ${PREFIX}Secr3t99")]
    [InlineData("password: \"${PREFIX}Secr3t99\"")]
    [InlineData("password: Secr3t99!")]
    public void TheFormsTheFourthReviewFound_AreFound(string text) => Assert.NotNull(SecretScan.Find(text));

    [Fact]
    public void AWholeReference_IsRedactedWhole()
    {
        Assert.Equal("password: [redacted]", SecretScan.Redact("password: ${DB_PASSWORD}"));
    }

    /// <summary>D391 (CR4-L3): variation selectors carry one byte each when appended to an emoji; an emoji keeps its own.</summary>
    [Theory]
    [InlineData("ok \U000E0100\U000E0101", true)]
    [InlineData("a\uFE0F", true)]
    [InlineData("👍\uFE0F\uFE01\uFE02", true)]
    [InlineData("\uFE0Fstart", true)]
    [InlineData("❤\uFE0F and 1\uFE0F\u20E3 and ™\uFE0F and ‼\uFE0F", false)]
    [InlineData("🏳\uFE0F\u200D🌈", false)]
    [InlineData("plain text, שלום, 👍🏽", false)]
    public void VariationSelectors_NotFollowingAnEmoji_AreHiddenText(string text, bool hidden) =>
        Assert.Equal(hidden, SecretScan.HasHiddenText(text));

    /// <summary>D391: whole documents of the kinds people and agents write, each with words a scanner could trip on.</summary>
    [Theory]
    [InlineData("""
        The orders table is partitioned by tenant: partition_key: tenant_id, sort_key: created_at. Rotate the billing
        API token every 90 days; never paste the token itself here. The tokenizer is cl100k_base and context_tokens: 128000.
        """)]
    [InlineData("""
        interface Credentials {
          username: string;
          password: string;
          apiKey?: string;
        }
        const apiKey = process.env.OPENAI_API_KEY;
        const token = await getToken(scope);
        export const config = { tokenUrl: "https://login.example.com/token", secretName: "postgres-credentials" };
        """)]
    [InlineData("""
        apiVersion: apps/v1
        kind: Deployment
        spec:
          template:
            spec:
              containers:
                - name: api
                  image: registry.example.com:5000/api@sha256:4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945
                  env:
                    - name: DB_PASSWORD
                      valueFrom:
                        secretKeyRef:
                          name: postgres-credentials
                          key: password
                    - name: API_TOKEN
                      value: ${API_TOKEN}
                    - name: LOG_LEVEL
                      value: debug-2
              volumes:
                - name: tls
                  secret:
                    secretName: api-tls
        """)]
    [InlineData("""
        auth:
          existingSecret: my-release-postgresql
          secretKeys:
            adminPasswordKey: postgres-password
            userPasswordKey: password
        primary:
          persistence:
            size: 8Gi
        """)]
    [InlineData("""
        ## Runbook: rotate the database password
        1. Generate a new one: `openssl rand -base64 24 > /run/secrets/db_password`.
        2. Log in: `docker login registry.example.com --password-stdin < ~/.docker/token`.
        3. Restart: `kubectl rollout restart deploy/api -n prod`.
        The password_file setting points at /run/secrets/db_password; the token_endpoint is https://login.example.com/token.
        If it fails, check `secret_ref: vault/prod/db` and the cookie_name: session_id setting.
        """)]
    public void EngineeringDocuments_AreNot(string text) => Assert.Null(SecretScan.Find(text));

    /// <summary>D391 (CR-L4): forms runbooks use that round 2 missed.</summary>
    [Theory]
    [InlineData("'password' => 'Secr3t99!'")]
    [InlineData(":password => \"Secr3t99!\"")]
    [InlineData("mysql --password Secr3t99 -h db")]
    [InlineData("vault login --token=s.Abc123xyz789")]
    [InlineData("curl -u admin:Secr3t99 https://api.example.com")]
    [InlineData("curl --user 'admin:Secr3t99' https://api.example.com")]
    [InlineData("<password>Secr3t99</password>")]
    [InlineData("<add key=\"x\"/><apiKey>AbC123xyz789</apiKey>")]
    [InlineData("password:          Secr3t99")]
    [InlineData("{\"name\":\"DB_PASSWORD\",\"value\":\"Secr3t99\"}")]
    [InlineData("- name: DB_PASSWORD\n  value: Secr3t99")]
    [InlineData("- name: \"API_TOKEN\"\n  value: \"AbC123xyz789\"")]
    [InlineData("tokenString=AbC123xyz789")]
    [InlineData("aws_secret_access_key: AbC123xyz789")]
    [InlineData("X-Auth-Token: AbC123xyz789")]
    [InlineData("spring.datasource.password=Secr3t99")]
    public void TheFormsTheThirdReviewFound_AreFound(string text) => Assert.NotNull(SecretScan.Find(text));

    [Fact]
    public void RedactJson_BlanksTheValueOfACredentialNamedEnvEntry()
    {
        var env = System.Text.Json.Nodes.JsonNode.Parse("""
            [{"name":"DB_PASSWORD","value":"Secr3t99"},{"name":"LOG_LEVEL","value":"debug"}]
            """);

        var line = SecretScan.RedactJson(env);

        Assert.DoesNotContain("Secr3t99", line);
        Assert.Contains("\"value\":\"debug\"", line);
    }

    [Theory]
    [InlineData("\"password\": \"hunter2,more\"", "more")]
    [InlineData("{\\\"apiKey\\\": \\\"abc, def\\\"}", "def")]
    [InlineData("Authorization: Bearer abc.def", "abc.def")]
    [InlineData("\"password\": \"it's-Secr3t99\"", "s-Secr3t99")]
    [InlineData("\"password\": \"ab\\\"cd-Secr3t99\"", "cd-Secr3t99")]
    [InlineData("{\\\"password\\\": \\\"ab\\\\\\\"cd-Secr3t99\\\"}", "cd-Secr3t99")]
    [InlineData("tokens_password='Summer 2024!'", "2024!")]
    [InlineData("keyring: x, password=Summer2024!", "Summer2024!")]
    public void AQuotedValue_IsBlankedWhole_CommasAndAll(string text, string tail)
    {
        var redacted = SecretScan.Redact(text);

        Assert.DoesNotContain(tail, redacted);
        Assert.Contains(SecretScan.Redacted, redacted);
    }

    [Fact]
    public void Counters_AreKept()
    {
        const string usage = "{\"prompt_tokens\": 812, \"max_tokens\": 4096, \"total_tokens\": 1200, \"token\": \"t0k3n-value\"}";

        var redacted = SecretScan.Redact(usage);

        Assert.Contains("\"total_tokens\": 1200", redacted);
        Assert.Contains("\"max_tokens\": 4096", redacted);
        Assert.DoesNotContain("t0k3n-value", redacted);
    }

    [Fact]
    public void Redact_ReadsAtMostItsLimit()
    {
        var redacted = SecretScan.Redact(string.Concat(Enumerable.Repeat("words ", 10_000)));

        Assert.InRange(redacted.Length, SecretScan.MaxRedacted - 6, SecretScan.MaxRedacted);
    }

    /// <summary>N7: a token cut by the limit fell below its minimum length and was logged all but one character.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(35)]
    public void ATokenStraddlingTheCut_IsDroppedWhole(int before)
    {
        const string token = "gh" + "p_0123456789abcdefghijklmnopqrstuvwxyzAB";
        var text = new string(' ', SecretScan.MaxRedacted - before) + token + " tail";

        var redacted = SecretScan.Redact(text);

        Assert.DoesNotContain(token[..before], redacted);
    }

    [Fact]
    public void APemKey_IsBlankedWithItsBody()
    {
        const string pem = "-----" + "BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEAabcdef\nZZZ0123456789+/=\n-----END RSA PRIVATE KEY-----\nafter";

        var redacted = SecretScan.Redact(pem);

        Assert.DoesNotContain("MIIEow", redacted);
        Assert.DoesNotContain("ZZZ0123", redacted);
        Assert.EndsWith("after", redacted);
    }

    [Fact]
    public void TagCharacters_AreStripped_AndFound()
    {
        Assert.True(SecretScan.HasHiddenText("hi\U000E0041\U000E0042"));
        Assert.False(SecretScan.HasHiddenText("hi 👍🏽 שלום"));
        Assert.Equal("ab", SecretScan.Redact("a\U000E0041\u200Bb"));
    }

    [Fact]
    public void AJsonError_IsRedactedBeforeItIsEscaped()
    {
        var error = System.Text.Json.Nodes.JsonNode.Parse("""
            {"code":-32000,"message":"bad spec","data":{"password":"+Summer2024","note":"Sum'mer2024abc",
             "conn":"\u0041ccountKey=abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz+1234567==",
             "auth":"Bearer abc+defghijklmnopqrstuvwxyz0123","max_tokens":4096}}
            """);

        var line = SecretScan.RedactJson(error);

        Assert.DoesNotContain("Summer2024", line);
        Assert.DoesNotContain("abc+def", line);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyzabcdefghij", line);
        Assert.Contains("\"max_tokens\":4096", line);
        Assert.Contains("bad spec", line);
        Assert.Contains("Sum'mer2024abc", line); // not named like a credential: kept, and not escaped
    }

    /// <summary>
    /// CR M1: 20,000 word characters timed the old named-value pattern out (1 s, then an exception out of a logging call).
    /// 200 KB of the worst shapes now takes well under a second for both calls.
    /// </summary>
    [Theory]
    [InlineData("a")]
    [InlineData("token")]
    [InlineData("password=")]
    [InlineData("gh" + "p_")]
    [InlineData("password=abcde ")]
    [InlineData("\"password\": \"ab ")]
    [InlineData("a: ")]
    [InlineData("--a ")]
    [InlineData("<a>")]
    [InlineData("name: a\n")]
    [InlineData("-u a:")]
    [InlineData("https://a:")]
    [InlineData("password=${")]
    [InlineData("password=${a}")]
    [InlineData("${a}")]
    [InlineData("Authorization: Token ")]
    [InlineData("password: a(")]
    [InlineData("password: /a")]
    public void TwoHundredKilobytes_AreLinear(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, 200_000 / unit.Length));
        SecretScan.Find("warm up");
        var clock = Stopwatch.StartNew();

        SecretScan.Find(text);
        SecretScan.Redact(text);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"took {clock.Elapsed}");
    }

    /// <summary>The mark SecretText and Redact leave is a placeholder, not a value (D391): a redacted card stays writable.</summary>
    [Theory]
    [InlineData("password: [redacted]")]
    [InlineData("\"apiKey\": \"[redacted]\"")]
    [InlineData("token=[redacted], next")]
    public void TheRedactionMark_IsNotACredential(string text) => Assert.Null(SecretScan.Find(text));

    /// <summary>What kagent echoes is logged through Redact (D391): each value is blanked.</summary>
    [Theory]
    [InlineData("password=hunter2-hunter2", "hunter2")]
    [InlineData("\"client_secret\": \"s3cr3t-value\"", "s3cr3t-value")]
    [InlineData("Cookie: skanyxx.auth=CfDJ8abc", "CfDJ8abc")]
    [InlineData("use skx_mem_AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdEfG now", "skx_mem_")]
    [InlineData("AK" + "IAABCDEFGHIJKLMNOP in the env", "AK" + "IAABCDEFGHIJKLMNOP")]
    public void Redact_BlanksTheValue(string text, string secret)
    {
        var redacted = SecretScan.Redact(text);

        Assert.DoesNotContain(secret, redacted);
        Assert.Contains(SecretScan.Redacted, redacted);
    }

    [Fact]
    public void Redact_LeavesOrdinaryTextAlone() =>
        Assert.Equal("agent ns/demo is not ready (3 replicas)", SecretScan.Redact("agent ns/demo is not ready (3 replicas)"));
}
