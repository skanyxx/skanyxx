using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Audit;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// D275: the audit explorer's API on a fixed set of rows inside a past hour — search, chips, free text, paging, the
/// histogram, field stats, analytics, export — its bounds, its role check, SQL injection attempts, and that no secret
/// leaves through any of it.
/// </summary>
public sealed class AuditExplorerTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string _owner = null!;
    private string _ownerId = null!;
    private string _member = null!;
    private string _memberId = null!;
    private DateTimeOffset _t;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
        _owner = (await App.SignInBearerAsync()).AccessToken;
        _ownerId = (await Owner().GetFromJsonAsync<JsonElement>("/api/identity/me", Ct)).GetProperty("id").GetString()!;
        var (tokens, memberId) = await App.AddMemberAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor);
        (_member, _memberId) = (tokens.AccessToken, memberId);

        var now = DateTimeOffset.UtcNow;
        _t = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-3);
        await InsertAsync(_t.AddMinutes(1), "person.disabled", _ownerId, _memberId, "10.0.0.1");
        await InsertAsync(_t.AddMinutes(2), "person.roles_changed", _ownerId, _memberId, "10.0.0.1", """{"added": ["builder"]}""");
        await InsertAsync(_t.AddMinutes(5), "route.owner_refused", _memberId, null, "10.0.0.2", """{"route": "api/identity/people"}""");
        await InsertAsync(_t.AddMinutes(10), "agent.write_failed", _memberId, "kagent/a1", "10.0.0.2");
        await InsertAsync(_t.AddMinutes(20), "signin.succeeded", _ownerId, _ownerId, null);
        await InsertAsync(_t.AddMinutes(21), "signin.refused", null, null, null, """{"skippedBefore": 4}""");
        await InsertAsync(_t.AddMinutes(30), "invite.created", _ownerId, null, "2001:db8::1", """{"note": "100% done"}""");
        await InsertAsync(_t.AddMinutes(59), "agent.created", _ownerId, "kagent/a1", null);
        await InsertAsync(_t.AddMinutes(-1), "person.enabled", _ownerId, _memberId, null);
        await InsertAsync(_t.AddMinutes(60), "person.enabled", _ownerId, _memberId, null);
    }

    [Fact]
    public async Task Search_IsTheTimeRange_NewestFirst_OrOldestFirst()
    {
        var desc = await GetAsync("search");
        var asc = await GetAsync("search", "sort=asc");

        Assert.Equal(8, desc.GetProperty("total").GetInt64());
        Assert.Equal("agent.created", Actions(desc)[0]);
        Assert.Equal("person.disabled", Actions(asc)[0]);
        Assert.Equal(Actions(desc).Reverse(), Actions(asc));
        Assert.Equal(JsonValueKind.Null, desc.GetProperty("next").ValueKind);
        var first = desc.GetProperty("entries")[0];
        Assert.Equal("agent", first.GetProperty("module").GetString());
        Assert.Equal("success", first.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Paging_WalksEveryHitOnce()
    {
        var seen = new List<long>();
        var sizes = new List<int>();
        var query = "limit=3";
        while (true)
        {
            var page = await GetAsync("search", query);
            var entries = page.GetProperty("entries").EnumerateArray().ToList();
            sizes.Add(entries.Count);
            seen.AddRange(entries.Select(e => e.GetProperty("id").GetInt64()));
            if (page.GetProperty("next").ValueKind == JsonValueKind.Null)
                break;
            var next = page.GetProperty("next");
            query = $"limit=3&afterAt={Uri.EscapeDataString(next.GetProperty("at").GetDateTimeOffset().ToString("O"))}&afterId={next.GetProperty("id").GetInt64()}";
        }

        Assert.Equal([3, 3, 2], sizes);
        Assert.Equal(8, seen.Distinct().Count());
    }

    [Theory]
    [InlineData("f=module:person", 2)]
    [InlineData("nf=module:person", 6)]
    [InlineData("f=actor:(none)", 1)]
    [InlineData("nf=actor:(none)", 7)]
    [InlineData("f=outcome:refused", 2)]
    [InlineData("f=outcome:failed", 1)]
    [InlineData("f=role:supervisor", 2)]
    [InlineData("nf=role:owner", 3)]
    [InlineData("f=role:(none)", 1)]
    [InlineData("f=ip:2001:db8::1", 1)]
    [InlineData("f=module:person&f=action:person.disabled", 1)]
    [InlineData("f=target:kagent/a1&nf=outcome:failed", 1)]
    [InlineData("q=100%25", 1)]
    [InlineData("q=%25", 1)]
    [InlineData("q=_", 3)]
    [InlineData("q=ROUTE", 1)]
    [InlineData("q=builder%40skanyxx", 4)]
    public async Task Chips_AndFreeText_Narrow(string query, int hits) =>
        Assert.Equal(hits, (await GetAsync("search", query)).GetProperty("total").GetInt64());

    [Fact]
    public async Task SqlInjection_IsJustText()
    {
        var dropped = await GetAsync("search", "q=" + Uri.EscapeDataString("'; DROP TABLE identity_audit; --"));
        var orTrue = await GetAsync("search", "f=" + Uri.EscapeDataString("action:x' OR '1'='1"));
        var field = await Owner().GetAsync(Url("search", "f=" + Uri.EscapeDataString("action\" OR 1=1 --:x")), Ct);
        var stats = await GetAsync("fields", "q=" + Uri.EscapeDataString("') UNION SELECT \"PasswordHash\", 1 FROM identity_users --"));

        Assert.Equal(0, dropped.GetProperty("total").GetInt64());
        Assert.Equal(0, orTrue.GetProperty("total").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, field.StatusCode);
        Assert.All(stats.EnumerateArray(), s => Assert.Empty(s.GetProperty("values").EnumerateArray()));
        Assert.Equal(8, (await GetAsync("search")).GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Histogram_IsOneMinuteBars_StackedByOutcome()
    {
        var histogram = await GetAsync("histogram");
        var buckets = histogram.GetProperty("buckets").EnumerateArray().ToList();
        long Sum(string outcome) => buckets.Sum(b => b.GetProperty(outcome).GetInt64());

        Assert.Equal(60, histogram.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(60, buckets.Count);
        Assert.Equal(_t, buckets[0].GetProperty("at").GetDateTimeOffset());
        Assert.Equal((5, 2, 1), (Sum("success"), Sum("refused"), Sum("failed")));
        Assert.Equal(1, buckets[5].GetProperty("refused").GetInt64());
        Assert.Equal(1, buckets[10].GetProperty("failed").GetInt64());
        Assert.Equal(1, buckets[59].GetProperty("success").GetInt64());
    }

    [Fact]
    public async Task FieldStats_AreTopValuesWithCounts()
    {
        var stats = (await GetAsync("fields")).EnumerateArray().ToDictionary(s => s.GetProperty("field").GetString()!);
        Dictionary<string, long> Values(string field) =>
            stats[field].GetProperty("values").EnumerateArray().ToDictionary(v => v.GetProperty("value").GetString()!, v => v.GetProperty("count").GetInt64());

        Assert.Equal(["action", "actor", "target", "module", "outcome", "ip", "role"], stats.Keys);
        Assert.All(stats.Values, s => Assert.Equal(8, s.GetProperty("total").GetInt64()));
        Assert.Equal(new Dictionary<string, long> { [_ownerId] = 5, [_memberId] = 2, ["(none)"] = 1 }, Values("actor"));
        Assert.Equal(new Dictionary<string, long> { ["owner"] = 5, ["supervisor"] = 2, ["(none)"] = 1 }, Values("role"));
        Assert.Equal(new Dictionary<string, long> { ["success"] = 5, ["refused"] = 2, ["failed"] = 1 }, Values("outcome"));
        Assert.Equal(2, Values("module")["agent"]);
        Assert.Equal(5, (await GetAsync("fields", "f=outcome:success")).EnumerateArray().First().GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Analytics_CountSignInsWithTheSampledOnes_AndAdminChangesByKind()
    {
        var analytics = await GetAsync("analytics");
        var signIns = analytics.GetProperty("signIns").EnumerateArray().ToList();
        var changes = analytics.GetProperty("adminChanges").EnumerateArray()
            .ToDictionary(v => v.GetProperty("value").GetString()!, v => v.GetProperty("count").GetInt64());

        Assert.Equal(1, signIns.Sum(b => b.GetProperty("succeeded").GetInt64()));
        Assert.Equal(5, signIns.Sum(b => b.GetProperty("failed").GetInt64())); // one row standing for itself and four skipped
        Assert.Equal(1, changes["roles"]);
        Assert.Equal(2, changes["people"]);
        Assert.Equal(1, changes["agents"]); // the failed write is not a change
        Assert.Equal(0, changes["models"]);
    }

    [Theory]
    [InlineData("search", "limit=201")]
    [InlineData("search", "limit=0")]
    [InlineData("search", "from=2020-01-01T00:00:00Z&to=2021-01-03T00:00:00Z")]
    [InlineData("histogram", "from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("fields", "f=password:x")]
    [InlineData("analytics", "f=nocolon")]
    [InlineData("export", "format=xml")]
    public async Task OutOfBounds_Is400(string route, string query)
    {
        var response = await Owner().GetAsync($"/api/identity/audit/{route}?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TooLongASearch_OrTooManyChips_Is400()
    {
        var longQ = await Owner().GetAsync(Url("search", "q=" + new string('a', 201)), Ct);
        var chips = await Owner().GetAsync(Url("search", string.Join("&", Enumerable.Range(0, 21).Select(i => $"f=action:a{i}"))), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, longQ.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, chips.StatusCode);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("histogram")]
    [InlineData("fields")]
    [InlineData("analytics")]
    [InlineData("export")]
    public async Task OnlyTheOwner_Reads(string route)
    {
        var anonymous = await App.Client().GetAsync($"/api/identity/audit/{route}", Ct);
        var supervisor = await App.Client(bearer: _member).GetAsync($"/api/identity/audit/{route}", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, supervisor.StatusCode);
        Assert.Empty(await Postgres.AuditAsync(AuditActions.Exported));
    }

    [Fact]
    public async Task Export_IsTheQuery_AsCsvOrJson_AndIsAudited()
    {
        var csv = await Owner().GetAsync(Url("export", "format=csv&f=module:person&sort=asc"), Ct);
        var json = await Owner().GetAsync(Url("export", "format=json&f=module:person"), Ct);
        var lines = (await csv.Content.ReadAsStringAsync(Ct)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var body = await json.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", csv.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(csv.Headers.CacheControl?.NoStore);
        Assert.Equal("false", csv.Headers.GetValues("X-Audit-Truncated").Single());
        Assert.StartsWith("\"time\",\"action\"", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\"person.disabled\"", lines[1]);
        Assert.Contains(IdentityApp.MemberEmail, lines[1]); // the target's email next to its id
        Assert.Equal(2, body.GetProperty("total").GetInt64());
        Assert.False(body.GetProperty("truncated").GetBoolean());
        var exports = await Postgres.AuditAsync(AuditActions.Exported);
        Assert.Equal(2, exports.Count);
        Assert.All(exports, e => Assert.Equal(_ownerId, e.ActorId));
        Assert.Contains("\"module:person\"", exports[0].Details);
        Assert.Contains("\"rows\": 2", exports[0].Details);
    }

    /// <summary>
    /// Fake secrets written through the audit command (redacted at write) and straight into the table (only the read
    /// redaction stands between them and the page) never come out of any route, nor out of the export's own row.
    /// </summary>
    [Fact]
    public async Task NoSecret_LeavesThroughAnyRoute()
    {
        await using (var scope = App.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RecordAuditCommand("test.secrets", _ownerId, null, new
            {
                password = "hunter2-fake",
                apiKey = "k-fake-123",
                note = "token=abc123secretvalue and sk-fakefakefakefakefake1234",
                connectionString = "Host=db;Password=pw-fake",
                prompt = "You are a secret system prompt",
                nested = new { authorization = "Bearer abcdefghijklmnopqrstuvwxyz", cards = new[] { new { body = "card body fake" } } },
                longText = new string('x', 1000)
            }), Ct);
        await InsertAsync(DateTimeOffset.UtcNow, "test.raw", _ownerId, null, null,
            """{"clientSecret": "raw-secret-fake", "message": "chat body fake", "free": "ghp_abcdefghijklmnopqrstuvwxyz0123456789"}""");
        string[] secrets =
        [
            "hunter2-fake", "k-fake-123", "abc123secretvalue", "sk-fakefakefakefakefake", "pw-fake", "secret system prompt",
            "abcdefghijklmnopqrstuvwxyz", "card body fake", "raw-secret-fake", "chat body fake", "gh" + "p_abcdefghij", new string('x', 300)
        ];
        var recent = "from=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"));
        var searched = "q=" + Uri.EscapeDataString("sk-fakefakefakefakefake9999");

        var stored = Assert.Single(await Postgres.AuditAsync("test.secrets")).Details!;
        var outputs = new List<string>
        {
            stored,
            await Owner().GetStringAsync(Url("search", recent), Ct),
            await Owner().GetStringAsync(Url("fields", recent), Ct),
            await Owner().GetStringAsync(Url("analytics", recent), Ct),
            await Owner().GetStringAsync(Url("export", recent + "&format=csv"), Ct),
            await Owner().GetStringAsync(Url("export", recent + "&format=json"), Ct),
            await Owner().GetStringAsync("/api/identity/audit", Ct),
            await Owner().GetStringAsync(Url("search", searched), Ct)
        };
        await Owner().GetStringAsync(Url("export", searched + "&format=json"), Ct);
        outputs.AddRange((await Postgres.AuditAsync(AuditActions.Exported)).Select(e => e.Details!));

        foreach (var secret in secrets)
            Assert.All(outputs, output => Assert.DoesNotContain(secret, output));
        Assert.DoesNotContain("sk-fakefakefakefakefake9999", outputs[^1]);
        Assert.Contains(SecretText.Mask, stored);
        Assert.Contains("[cut, 1000 chars]", stored);
        Assert.Contains(SecretText.Mask, outputs[1]);
        Assert.Contains("test.raw", outputs[1]);
    }

    [Fact]
    public async Task SignIns_AreAudited_AndARefusalNamesNobody()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.SignInAsync(IdentityApp.OwnerEmail, "not the password")).StatusCode);

        var succeeded = await Postgres.AuditAsync(AuditActions.SignedIn);
        var refused = Assert.Single(await Postgres.AuditAsync(AuditActions.SignInRefused), r => r.AtUtc > _t.AddHours(1)); // not the seeded one

        Assert.Contains(succeeded, r => r.ActorId == _ownerId && r.Details!.Contains("password"));
        Assert.Null(refused.ActorId);
        Assert.Null(refused.TargetId);
        Assert.DoesNotContain(IdentityApp.OwnerEmail, refused.Details);
        Assert.Equal("refused", refused.Outcome);
        Assert.Equal("signin", refused.Module);
    }

    [Fact]
    public async Task TheMigration_AddsGeneratedColumnsAndIndexes()
    {
        await using var db = Postgres.CreateDbContext();
        var generated = await db.Database.SqlQuery<string>(
            $"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'identity_audit' AND is_generated = 'ALWAYS' ORDER BY 1").ToListAsync(Ct);
        var indexes = await db.Database.SqlQuery<string>(
            $"SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'identity_audit'").ToListAsync(Ct);

        Assert.Equal(["Module", "Outcome"], generated);
        Assert.Contains("IX_identity_audit_Module_AtUtc", indexes);
        Assert.Contains("IX_identity_audit_Outcome_AtUtc", indexes);
        Assert.Contains("IX_identity_audit_RemoteIp", indexes);
    }

    private HttpClient Owner() => App.Client(bearer: _owner);

    /// <summary>The route over the seeded hour, unless the query names its own <c>from</c>.</summary>
    private string Url(string route, string query = "") => $"/api/identity/audit/{route}?" + (query.StartsWith("from=")
        ? query
        : $"from={Uri.EscapeDataString(_t.ToString("O"))}&to={Uri.EscapeDataString(_t.AddHours(1).ToString("O"))}&{query}");

    private async Task<JsonElement> GetAsync(string route, string query = "")
    {
        var response = await Owner().GetAsync(Url(route, query), Ct);
        Assert.True(response.IsSuccessStatusCode, $"{route}?{query}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static string[] Actions(JsonElement page) =>
        [.. page.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("action").GetString()!)];

    private async Task InsertAsync(DateTimeOffset at, string action, string? actor, string? target, string? ip, string? details = null)
    {
        await using var db = Postgres.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_audit ("AtUtc", "Action", "ActorId", "TargetId", "RemoteIp", "Details")
            VALUES ({at}, {action}, {actor}, {target}, {ip}, CAST({details} AS jsonb))
            """, Ct);
    }
}
