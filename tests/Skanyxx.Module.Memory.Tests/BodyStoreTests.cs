using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Features.Cards;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// D180–D184 against a real MinIO (the chart's image): a body over the threshold goes to the bucket under its SHA-256,
/// Postgres keeps an excerpt and <c>source</c> the pointer; agents never see either (D014); a person who may open the
/// card reads the full body from <c>/body</c>. Threshold 1024 bytes. Org: ana is in team billing, bob in nothing.
/// </summary>
[Collection(ObjectStoreCollection.Name)]
public sealed class BodyStoreTests(PostgresFixture postgres, MinioFixture minio) : IAsyncLifetime
{
    private const string Head = "HEAD-OF-THE-BODY ";
    private const string Tail = " TAIL-ONLY-IN-THE-BUCKET";

    private readonly string _bucket = minio.NewBucket();
    private MemoryApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await MemoryApp.StartAsync(postgres.ConnectionString, extra: minio.Settings(_bucket));
        _app.Org.Join("ana", "billing", "finance");
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Over the 1024-byte threshold; the tail lies beyond the 500-character excerpt.</summary>
    private static string LargeBody(char fill = 'x') => Head + new string(fill, 3000) + Tail;

    private static string Pointer(string bucket, string body) =>
        $"s3://{bucket}/cards/sha256/{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))}";

    private static Task<HttpResponseMessage> BodyAsync(HttpClient client, string scope, string key) =>
        client.GetAsync($"/api/memory/cards/{scope}/{key}/body", Ct);

    [Fact]
    public async Task BelowThreshold_StaysInPostgres_AndTheBucketIsNotTouched()
    {
        var body = new string('y', 1024);

        var card = await (await _app.Client("ana").PutCardAsync("personal:ana", "small", body: body, source: "chat://1")).CardAsync();

        Assert.Equal(body, card.Body);
        Assert.Equal("chat://1", card.Source);
        Assert.Empty(await minio.KeysAsync(_bucket));
    }

    [Fact]
    public async Task AboveThreshold_TheBodyGoesToTheBucket_PostgresKeepsAnExcerptAndThePointer()
    {
        var body = LargeBody();

        var response = await _app.Client("ana").PutCardAsync("personal:ana", "big", body: body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var card = await response.CardAsync();
        Assert.Equal(body[..CardLimits.BodyExcerpt], card.Body);
        Assert.Equal(Pointer(_bucket, body), card.Source);
        Assert.Matches("^s3://[a-z0-9.-]+/cards/sha256/[0-9a-f]{64}$", card.Source);
        var key = Assert.Single(await minio.KeysAsync(_bucket)); // the app created the bucket on this first write (D184)
        Assert.Equal(body, await minio.ReadAsync(_bucket, key));
        await using var db = postgres.CreateDbContext();
        Assert.DoesNotContain(db.Cards, c => c.Body!.Contains(Tail));
    }

    [Fact]
    public async Task TheExcerpt_NeverSplitsASurrogatePair()
    {
        var body = new string('a', CardLimits.BodyExcerpt - 1) + string.Concat(Enumerable.Repeat("😀", 600));

        var card = await (await _app.Client("ana").PutCardAsync("personal:ana", "emoji", body: body)).CardAsync();

        Assert.Equal(CardLimits.BodyExcerpt - 1, card.Body!.Length);
        Assert.Equal(body, await (await BodyAsync(_app.Client("ana"), "personal:ana", "emoji")).Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task McpAndSearch_NeverCarryTheBlob_TheExcerptOrThePointer()
    {
        await _app.SupervisorClient().PutCardAsync("company", "refund-window", body: LargeBody());
        await using var client = await _app.McpAsync("seed");

        var result = await client.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" }, cancellationToken: Ct);
        var rest = JsonSerializer.Serialize(await _app.Client("ana").SearchAsync("refund"));

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
                   (result.StructuredContent is { } s ? JsonSerializer.Serialize(s) : "");
        Assert.Contains("refund-window", text);
        foreach (var output in new[] { text, rest })
        {
            Assert.DoesNotContain("HEAD-OF-THE-BODY", output);
            Assert.DoesNotContain("TAIL-ONLY", output);
            Assert.DoesNotContain("s3://", output);
            Assert.DoesNotContain("sha256", output);
        }
    }

    [Fact]
    public async Task McpUpsert_OfWhatAndWhy_KeepsTheStoredBodyAndPointer()
    {
        var body = LargeBody();
        var boss = _app.SupervisorClient();
        await boss.SetGrantsAsync("writer", new { scope = "company", canSearch = true, canUpsert = true });
        await boss.PutCardAsync("company", "refund-window", body: body);
        await using var client = await _app.McpAsync("writer");

        await client.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "refund-window", ["type"] = "decision", ["what"] = "We refund within 30 days", ["why"] = "Policy Y",
            ["version"] = 1, ["scope"] = "company"
        }, cancellationToken: Ct);

        var card = await (await boss.GetAsync("/api/memory/cards/company/refund-window", Ct)).CardAsync();
        Assert.Equal(2, card.Version);
        Assert.Equal(Pointer(_bucket, body), card.Source);
        Assert.Equal(body, await (await BodyAsync(boss, "company", "refund-window")).Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Body_IsServedAsInertPlainText_ToWhoeverMayOpenTheCard()
    {
        var body = LargeBody();
        await _app.Client("ana").PutCardAsync("team:billing", "close", body: body);

        var member = await BodyAsync(_app.Client("ana"), "team:billing", "close");
        var outsider = await BodyAsync(_app.Client("bob"), "team:billing", "close");
        var anonymous = await BodyAsync(_app.Client(), "team:billing", "close");

        Assert.Equal(HttpStatusCode.OK, member.StatusCode);
        Assert.Equal(body, await member.Content.ReadAsStringAsync(Ct));
        Assert.Equal("text/plain", member.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", member.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox; default-src 'none'", member.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(member.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal(Encoding.UTF8.GetByteCount(body), member.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
        Assert.DoesNotContain("TAIL-ONLY", await outsider.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Body_OfAPersonalCard_IsTheOwnersOnly_AndADraftCompanyCardsIsNotAnyonesToRead()
    {
        await _app.Client("ana").PutCardAsync("personal:ana", "diary", body: LargeBody());
        await _app.SupervisorClient().PutCardAsync("company", "draft", body: LargeBody('d'));
        await using (var db = postgres.CreateDbContext())
        {
            var draft = db.Cards.Single(c => c.Key == "draft");
            draft.Status = CardStatus.Candidate;
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(HttpStatusCode.OK, (await BodyAsync(_app.Client("ana"), "personal:ana", "diary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await BodyAsync(_app.Client("bob"), "personal:ana", "diary")).StatusCode);
        // D103: an unpublished company card is a missing card to anyone but those who may write company.
        Assert.Equal(HttpStatusCode.NotFound, (await BodyAsync(_app.Client("ana"), "company", "draft")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BodyAsync(_app.SupervisorClient(), "company", "draft")).StatusCode);
    }

    [Fact]
    public async Task Body_OfAnInlineCard_IsThePostgresBody_AndACardWithoutOneIs404()
    {
        var ana = _app.Client("ana");
        await ana.PutCardAsync("personal:ana", "small", body: "short body");
        await ana.PutCardAsync("personal:ana", "bare");

        Assert.Equal("short body", await (await BodyAsync(ana, "personal:ana", "small")).Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await BodyAsync(ana, "personal:ana", "bare")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BodyAsync(ana, "personal:ana", "nothing")).StatusCode);
    }

    [Fact]
    public async Task Lift_SharesThePointer_AndTheCopysReadersReadTheBody()
    {
        var body = LargeBody();
        var ana = _app.Client("ana");
        await ana.PutCardAsync("personal:ana", "refund-window", body: body);
        _app.Org.Join("carol", "billing", "finance");

        var copy = await (await ana.LiftAsync("personal:ana", "refund-window", "team:billing")).CardAsync();

        Assert.Equal(Pointer(_bucket, body), copy.Source);
        Assert.Equal(body[..CardLimits.BodyExcerpt], copy.Body);
        Assert.Single(await minio.KeysAsync(_bucket)); // one object, two cards (D182)
        Assert.Equal(body, await (await BodyAsync(_app.Client("carol"), "team:billing", "refund-window")).Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await BodyAsync(_app.Client("carol"), "personal:ana", "refund-window")).StatusCode);
    }

    [Fact]
    public async Task Rename_KeepsThePointer_AndTheBodyFollowsTheNewKey()
    {
        var body = LargeBody();
        var ana = _app.Client("ana");
        await ana.PutCardAsync("personal:ana", "old-name", body: body);

        var renamed = await (await ana.PostAsJsonAsync("/api/memory/cards/personal:ana/old-name/rename", new { newKey = "new-name", version = 1 }, Ct)).CardAsync();

        Assert.Equal(Pointer(_bucket, body), renamed.Source);
        Assert.Equal(body, await (await BodyAsync(ana, "personal:ana", "new-name")).Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await BodyAsync(ana, "personal:ana", "old-name")).StatusCode);
    }

    [Fact]
    public async Task NewVersions_MoveThePointer_ARoundTripChangesNothing_AndASmallBodyComesHome()
    {
        var first = LargeBody('1');
        var second = LargeBody('2');
        var ana = _app.Client("ana");
        var v1 = await (await ana.PutCardAsync("personal:ana", "policy", body: first)).CardAsync();

        var v2 = await (await ana.PutCardAsync("personal:ana", "policy", version: 1, body: second)).CardAsync();
        // A client writing back exactly what it read (the excerpt and the pointer) changes neither.
        var v3 = await (await ana.PutCardAsync("personal:ana", "policy", version: 2, what: "edited", body: v2.Body, source: v2.Source)).CardAsync();
        var v3Body = await (await BodyAsync(ana, "personal:ana", "policy")).Content.ReadAsStringAsync(Ct);
        var v4 = await (await ana.PutCardAsync("personal:ana", "policy", version: 3, body: "short again")).CardAsync();

        Assert.Equal(Pointer(_bucket, first), v1.Source);
        Assert.True(v1.BodyStored);
        Assert.Equal(Pointer(_bucket, second), v2.Source);
        Assert.Equal(v2.Source, v3.Source);
        Assert.Equal("edited", v3.What);
        Assert.Equal(second, v3Body);
        Assert.Equal("short again", v4.Body);
        Assert.Null(v4.Source);
        Assert.False(v4.BodyStored);
        Assert.Equal(4, v4.Version);
        Assert.Equal(2, (await minio.KeysAsync(_bucket)).Count); // older bodies stay in the bucket (D182)
    }

    [Fact]
    public async Task AnEditedExcerpt_WrittenBackWithThePointer_IsRefused_AndSourceEmptyReplacesTheBodyOnPurpose()
    {
        var body = LargeBody();
        var ana = _app.Client("ana");
        await ana.PutCardAsync("personal:ana", "policy", body: body);
        var read = await (await ana.GetAsync("/api/memory/cards/personal:ana/policy", Ct)).CardAsync();

        // A REST client's read-modify-write: it fixes a typo in what it read and sends the pointer back with it.
        var edited = await ana.PutCardAsync("personal:ana", "policy", version: 1, body: read.Body + " (typo fixed)", source: read.Source);
        var afterRefusal = await (await ana.GetAsync("/api/memory/cards/personal:ana/policy", Ct)).CardAsync();
        var stillFull = await (await BodyAsync(ana, "personal:ana", "policy")).Content.ReadAsStringAsync(Ct);
        var replaced = await ana.PutCardAsync("personal:ana", "policy", version: 1, body: "short on purpose", source: "");

        Assert.True(read.BodyStored);
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        var errors = (await edited.JsonAsync()).GetProperty("errors");
        Assert.Contains("/body", errors.GetProperty("body")[0].GetString());
        Assert.Equal(1, afterRefusal.Version);
        Assert.Equal(Pointer(_bucket, body), afterRefusal.Source);
        Assert.Equal(body, stillFull);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var card = await replaced.CardAsync();
        Assert.Equal("short on purpose", card.Body);
        Assert.True(string.IsNullOrEmpty(card.Source));
        Assert.False(card.BodyStored);
    }

    [Fact]
    public async Task WrongCredentials_AreStoreUnavailable_ForAWriteAndForBody_WithNothingSaved()
    {
        await _app.Client("ana").PutCardAsync("personal:ana", "stored", body: LargeBody());
        var settings = minio.Settings(_bucket);
        settings["Memory:ObjectStore:SecretKey"] = "not-the-secret-key";
        await using var app = await MemoryApp.StartAsync(postgres.ConnectionString, extra: settings);

        var write = await app.Client("ana").PutCardAsync("personal:ana", "big", body: LargeBody('w'));
        var read = await BodyAsync(app.Client("ana"), "personal:ana", "stored");

        // An S3 *service* error (403 SignatureDoesNotMatch) is not an AmazonClientException in AWSSDK v4: it was a 500.
        Assert.Equal(HttpStatusCode.BadGateway, write.StatusCode);
        Assert.Contains("not saved", await write.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadGateway, read.StatusCode);
        Assert.Equal(1, await postgres.CardCountAsync());
        Assert.Single(await minio.KeysAsync(_bucket));
    }

    [Fact]
    public async Task LoneSurrogates_AreRefused_NotStoredAsReplacementCharacters()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var refused = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => mediator.Send(
            new UpsertCardCommand(MemoryCaller.For(People.Person("ana")), "personal:ana", "lone", 0, "fact", "w", "y",
                Body: LargeBody() + "\uD800"), Ct));
        var lowFirst = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => mediator.Send(
            new UpsertCardCommand(MemoryCaller.For(People.Person("ana")), "personal:ana", "lone", 0, "fact", "w", "y",
                Body: "\uDC00\uD800 short"), Ct));
        var paired = await mediator.Send(
            new UpsertCardCommand(MemoryCaller.For(People.Person("ana")), "personal:ana", "paired", 0, "fact", "w", "y",
                Body: LargeBody() + "\uD83D\uDE00"), Ct);

        Assert.Contains(refused.Errors, e => e.ErrorMessage.Contains("surrogate"));
        Assert.Contains(lowFirst.Errors, e => e.ErrorMessage.Contains("surrogate"));
        Assert.Equal(OutcomeStatus.Created, paired.Status);
        Assert.Equal(1, await postgres.CardCountAsync());
        Assert.Single(await minio.KeysAsync(_bucket));
    }

    [Fact]
    public async Task Sources_ThatWouldLoseALinkOrForgeAPointer_AreRefused()
    {
        var ana = _app.Client("ana");
        var forged = await ana.PutCardAsync("personal:ana", "forged", source: Pointer(_bucket, "someone else's body"));
        var both = await ana.PutCardAsync("personal:ana", "both", body: LargeBody(), source: "https://wiki/page");
        await ana.PutCardAsync("personal:ana", "linked", body: "short", source: "chat://42");
        var losesLink = await ana.PutCardAsync("personal:ana", "linked", version: 1, body: LargeBody());
        var dropsLink = await ana.PutCardAsync("personal:ana", "linked", version: 1, body: LargeBody(), source: "");
        await ana.PutCardAsync("personal:ana", "stored", body: LargeBody());
        var detaches = await ana.PutCardAsync("personal:ana", "stored", version: 1, source: "chat://7");

        foreach (var refused in new[] { forged, both, losesLink, detaches })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.True((await refused.JsonAsync()).GetProperty("errors").TryGetProperty("source", out _));
        }
        Assert.Equal(HttpStatusCode.OK, dropsLink.StatusCode);
        Assert.StartsWith($"s3://{_bucket}/", (await dropsLink.CardAsync()).Source);
    }

    [Fact]
    public async Task ABodyOverTheGroupsWireCap_IsAccepted_UpToMaxBodyBytes()
    {
        var ana = _app.Client("ana");
        var big = new string('z', 300 * 1024); // more than the 256 KB every other memory request may send

        var accepted = await ana.PutCardAsync("personal:ana", "huge", body: big);
        var tooBig = await ana.PutCardAsync("personal:ana", "too-big", body: new string('z', 1024 * 1024 + 1));
        var otherRoute = await ana.PostAsJsonAsync("/api/memory/cards/personal:ana/huge/rename", new { newKey = big, version = 1 }, Ct);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(big, await (await BodyAsync(ana, "personal:ana", "huge")).Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, otherRoute.StatusCode); // only the upsert's cap grows
    }

    // FastEndpoints resolves an endpoint's Config through a process-wide static that every app overwrites while it maps
    // its endpoints, so a cap decided in Configure() could come from whichever app was being built alongside: the
    // flaky 413. The cap is now this app's own, decided per request.
    [Fact]
    public async Task TheUpsertsCap_IsThisAppsOwn_WhileStoreOffAppsAreBuiltAlongside()
    {
        var big = new string('z', 300 * 1024);
        var on = MemoryApp.Settings(postgres.ConnectionString, extra: minio.Settings(_bucket));
        var off = MemoryApp.Settings(postgres.ConnectionString);

        for (var round = 0; round < 5; round++)
        {
            var built = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => MemoryApp.Build(i == 4 ? on : off))));
            await using var app = await MemoryApp.StartAsync(built[4]);
            foreach (var other in built.Where((_, i) => i != 4))
                await other.DisposeAsync();

            var response = await app.Client("ana").PutCardAsync("personal:ana", $"huge-{round}", body: big);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    [Fact]
    public async Task TheLibrary_MarksAStoredBody()
    {
        var ana = _app.Client("ana");
        await ana.PutCardAsync("personal:ana", "big", body: LargeBody());
        await ana.PutCardAsync("personal:ana", "small", body: "short");

        await using var scope = _app.Services.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var big = (await mediator.Send(new OpenCardQuery(People.Person("ana"), "personal:ana", "big"), Ct)).Value!;
        var small = (await mediator.Send(new OpenCardQuery(People.Person("ana"), "personal:ana", "small"), Ct)).Value!;

        Assert.True(big.Card.BodyStored);
        Assert.False(small.Card.BodyStored);
    }

    [Fact]
    public async Task AnUnreachableStore_RefusesTheWrite_WithNothingSaved()
    {
        var settings = minio.Settings(_bucket);
        settings["Memory:ObjectStore:Endpoint"] = "http://127.0.0.1:1";
        await using var app = await MemoryApp.StartAsync(postgres.ConnectionString, extra: settings);

        var response = await app.Client("ana").PutCardAsync("personal:ana", "big", body: LargeBody());
        var small = await app.Client("ana").PutCardAsync("personal:ana", "small", body: "short");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, small.StatusCode);
        Assert.Equal(1, await postgres.CardCountAsync());
    }

    [Fact]
    public async Task AStoredBody_WhenTheStoreIsTurnedOff_Is502_NotItsExcerpt()
    {
        await _app.Client("ana").PutCardAsync("personal:ana", "big", body: LargeBody());
        await using var off = await MemoryApp.StartAsync(postgres.ConnectionString);

        var response = await BodyAsync(off.Client("ana"), "personal:ana", "big");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Theory]
    [InlineData("Memory:ObjectStore:AccessKey", "")]
    [InlineData("Memory:ObjectStore:Bucket", "Not_A_Bucket")]
    [InlineData("Memory:ObjectStore:Endpoint", "ftp://minio:9000")]
    [InlineData("Memory:ObjectStore:ThresholdBytes", "20001")]
    [InlineData("Memory:ObjectStore:MaxBodyBytes", "1000")]
    [InlineData("Memory:ObjectStore:MaxBodyBytes", "8388609")] // above the 8 MiB ceiling
    public async Task AnIncompleteStoreConfig_StopsStartup(string key, string value)
    {
        var settings = minio.Settings(_bucket);
        settings["ConnectionStrings:Memory"] = postgres.ConnectionString;
        settings[key] = value;
        await using var app = MemoryApp.Build(settings);

        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() => app.StartAsync(Ct));
    }
}
