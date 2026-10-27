using System.Net;
using System.Net.Http.Json;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// C-1, requirements.md §1.4: a credential typed into a chat (or a run's output) never comes back out of memory — not in
/// the card API, its history, a lifted copy, a search hit or a prompt. All values here are synthetic.
/// </summary>
public sealed class CardSecretTests(PostgresFixture postgres) : MemoryTestBase(postgres)
{
    private const string Marker = "SYNTHSECRET";
    private const string Question = "Deploy failing for SDB-58, my api key is sk-test-0123456789abcdef" + Marker +
                                    " and password: hunter2-" + Marker + ", repo https://bob:pw" + Marker + "@git.example/x";
    private const string Answer = "Rotate it. I used token=abc123" + Marker + "xyz and Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0." + Marker + "sig";

    [Fact]
    public async Task AChatTurnWithCredentials_KeepsACard_WithoutThem_AnywhereItIsRead()
    {
        App.Org.Join("ana", "billing", "finance");
        var card = await App.ChatTurnAsync(new ChatTurnDigest("ana", "seed", "kagent/seed", "s-secret", Question, Answer, true));
        await App.ChatTurnAsync(new ChatTurnDigest("ana", "seed", "kagent/seed", "s-secret", "and again?", Answer + " password=x" + Marker, false));
        var ana = App.Client("ana");

        var read = await (await ana.GetAsync($"/api/memory/cards/personal:ana/{card!.Key}", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var history = await (await ana.GetAsync($"/api/memory/cards/personal:ana/{card.Key}/history", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var lifted = await (await ana.LiftAsync("personal:ana", card.Key, "team:billing")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var search = System.Text.Json.JsonSerializer.Serialize(await ana.SearchAsync("deploy failing SDB-58"));
        var prompt = (await App.RelatedAsync("ana", "seed", "deploy failing SDB-58")).Block;

        Assert.Contains("SDB-58", read);
        Assert.Contains(SecretText.Mask, read);
        foreach (var text in new[] { read, history, lifted, search, prompt })
            Assert.DoesNotContain(Marker, text);
        Assert.NotEmpty(prompt);
    }

    [Fact]
    public async Task ARunOutcomeWithCredentials_IsRedacted()
    {
        await App.TicketRunAsync(new TicketRunFinished(Guid.NewGuid(), "ana", "SDB-58", "Fix login sk-live-abcdefghijklmnop" + Marker,
            "https://u:p" + Marker + "@jira.example/browse/SDB-58", "default", true, "QA (pass): set client_secret=" + Marker + "123", [], []));

        await using var db = Postgres.CreateDbContext();
        var card = db.Cards.Single();
        Assert.DoesNotContain(Marker, card.What + card.Why + string.Join(",", card.Links) + string.Join(",", card.Keywords));
        Assert.Empty(card.Links);
    }

    [Fact]
    public async Task AWriteCarryingACredential_IsRefused_OverRestAndMcp()
    {
        var rest = await App.Client("ana").PutCardAsync("personal:ana", "leak", why: "use sk-test-0123456789abcdef" + Marker);
        await using var mcp = await App.McpAsync("seed", userId: Users.Ana);
        var tool = await mcp.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "leak", ["type"] = "fact", ["what"] = "ghp_0123456789abcdefghij" + Marker, ["why"] = "x"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, rest.StatusCode);
        Assert.True(tool.IsError);
        Assert.Equal(0, await Postgres.CardCountAsync());
        // Prose about passwords is not a credential.
        Assert.Equal(HttpStatusCode.Created,
            (await App.Client("ana").PutCardAsync("personal:ana", "policy", what: "Password policy: 12 characters minimum")).StatusCode);
    }

    [Theory]
    [InlineData("key sk-proj-abcdefghijklmnopqrstuv end")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    [InlineData("xoxb-1234567890-abcdefghij")]
    [InlineData("skx_mem_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIabc\n-----END RSA PRIVATE KEY-----")]
    [InlineData("password = \"correct horse\"")]
    [InlineData("Server=db;User Id=sa;Password=Pa55word;")]
    [InlineData("Authorization: Bearer abcdefghijklmnop12345")]
    [InlineData("hash 3f786850e387550fdab836ed7e6dc881de23001b9a1e2b3c")]
    public void Redact_MasksEveryCredentialShape(string text)
    {
        var redacted = SecretText.Redact(text);
        Assert.Contains(SecretText.Mask, redacted);
        Assert.DoesNotContain("abcdefghij", redacted);
        Assert.DoesNotContain("Pa55word", redacted);
        Assert.DoesNotContain("correct horse", redacted);
    }

    [Fact]
    public void Redact_LeavesOrdinaryProseAndTicketKeys()
    {
        const string text = "We refund within 14 days for SDB-58; see https://wiki.example/refunds and session 0a0a0a0a-0000-4000-8000-000000000001.";
        Assert.Equal(text, SecretText.Redact(text));
    }
}
