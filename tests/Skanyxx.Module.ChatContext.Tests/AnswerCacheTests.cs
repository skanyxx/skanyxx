using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Runtime;
using Skanyxx.Module.ChatContext.Cache;
using Skanyxx.Module.ChatContext.Tests.Infrastructure;
using static Skanyxx.Module.ChatContext.Tests.Infrastructure.People;

namespace Skanyxx.Module.ChatContext.Tests;

/// <summary>D241: the answer cache — hit, miss, scope isolation and invalidation — against an in-memory Redis.</summary>
public sealed class AnswerCacheTests(PostgresFixture postgres) : ChatContextTestBase(postgres)
{
    private const string Question = "What is our refund window?";

    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ChatContext:AnswerCache:Enabled"] = "true",
        ["ChatContext:AnswerCache:Redis"] = "unused:6379"
    };

    /// <summary>A first turn that misses, then is answered by "kagent" and kept.</summary>
    private async Task AskedAndAnsweredAsync(string userId, string sessionId, string question = Question, string revision = "1")
    {
        var prepared = await App.PrepareAsync(userId, question, revision: revision);
        Assert.Null(prepared.Cached);
        await App.TurnAsync(userId, sessionId, question, "Fourteen days.", cacheKey: prepared.CacheKey);
    }

    [Fact]
    public async Task TheSameQuestion_IsAnsweredFromTheCache_InTheOriginalConversation()
    {
        await AskedAndAnsweredAsync(Ana, "s1");

        var again = await App.PrepareAsync(Ana, "  what is our REFUND   window ");

        Assert.NotNull(again.Cached);
        Assert.True(again.Cached.Cached);
        Assert.Equal("s1", again.Cached.ConversationId);
        Assert.Equal("Fourteen days.", again.Cached.Message.Content);
        var stats = await App.Client(Ana).GetFromJsonAsync<AnswerCacheStats>("/api/chat/cache", ChatContextApp.Json, TestContext.Current.CancellationToken);
        Assert.Equal(1, stats!.Hits);
        Assert.True(stats.TokensSaved > 0);
        Assert.Null(stats.AllHits);
    }

    [Fact]
    public async Task AnAnswer_IsNeverServedToAnotherPerson()
    {
        await AskedAndAnsweredAsync(Ana, "s1");

        Assert.Null((await App.PrepareAsync(Bob, Question)).Cached);
        Assert.Null((await App.PrepareAsync(Boss, Question)).Cached);
    }

    [Fact]
    public async Task ADifferentAgent_Revision_TicketOrAFreshAsk_Misses()
    {
        await AskedAndAnsweredAsync(Ana, "s1");

        Assert.Null((await App.PrepareAsync(Ana, Question, agent: "team-a/seed")).Cached);
        Assert.Null((await App.PrepareAsync(Ana, Question, revision: "2")).Cached);
        Assert.Null((await App.PrepareAsync(Ana, Question, ["SDB-58"])).Cached);
        var fresh = await App.PrepareAsync(Ana, Question, noCache: true);
        Assert.Null(fresh.Cached);
        Assert.Null(fresh.CacheKey);
    }

    [Fact]
    public async Task ASharedCardWrite_InvalidatesEveryone_APersonalOne_OnlyItsOwner()
    {
        await AskedAndAnsweredAsync(Ana, "a1");
        await AskedAndAnsweredAsync(Bob, "b1");

        await App.CardAsync(Bob, "personal:bob", "note", "Bob's note", "Why");
        Assert.NotNull((await App.PrepareAsync(Ana, Question)).Cached);
        Assert.Null((await App.PrepareAsync(Bob, Question)).Cached);

        await App.CardAsync(Boss, "company", "refund-window", "We refund within 30 days", "New policy");
        Assert.Null((await App.PrepareAsync(Ana, Question)).Cached);
    }

    [Fact]
    public async Task ClearingMine_OrForgettingTheSession_Invalidates()
    {
        await AskedAndAnsweredAsync(Ana, "s1");
        (await App.Client(Ana).DeleteAsync("/api/chat/cache", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Null((await App.PrepareAsync(Ana, Question)).Cached);

        await AskedAndAnsweredAsync(Ana, "s2", "Another question?");
        (await App.Client(Ana).DeleteAsync("/api/chat/sessions/s2", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Null((await App.PrepareAsync(Ana, "Another question?")).Cached);
    }

    [Fact]
    public async Task OnlyTheOwnerClearsEveryone_AndAModelChangeDoesToo()
    {
        await AskedAndAnsweredAsync(Ana, "s1");
        Assert.Equal(HttpStatusCode.Forbidden, (await App.Client(Boss).DeleteAsync("/api/chat/cache?all=true", TestContext.Current.CancellationToken)).StatusCode);
        Assert.NotNull((await App.PrepareAsync(Ana, Question)).Cached);
        (await App.Client(Olivia).DeleteAsync("/api/chat/cache?all=true", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Null((await App.PrepareAsync(Ana, Question)).Cached);

        await AskedAndAnsweredAsync(Ana, "s2");
        await using (var scope = App.Services.CreateAsyncScope())
        {
            var behavior = scope.ServiceProvider.GetServices<IPipelineBehavior<SaveModelSettingsCommand, Outcome<ModelSettings>>>().OfType<ModelChangeClearsAnswers>().Single();
            await behavior.Handle(new SaveModelSettingsCommand(Olivia, "Ollama", "qwen3", null, null),
                () => Task.FromResult(Outcome<ModelSettings>.Ok(new ModelSettings("kagent/default", "Ollama", "qwen3", null, false, true, null))), CancellationToken.None);
        }
        Assert.Null((await App.PrepareAsync(Ana, Question)).Cached);
    }

    [Fact]
    public async Task OnlyFirstTurns_AreKept()
    {
        var prepared = await App.PrepareAsync(Ana, Question);
        await App.TurnAsync(Ana, "s1", Question, "Fourteen days.", first: false, cacheKey: prepared.CacheKey);

        Assert.Null((await App.PrepareAsync(Ana, Question)).Cached);
    }

    [Fact]
    public async Task ACacheThatIsDown_IsAMiss_AndChatGoesOn()
    {
        await AskedAndAnsweredAsync(Ana, "s1");
        App.Store.Down = true;

        var prepared = await App.PrepareAsync(Ana, Question);
        await App.TurnAsync(Ana, "s2", Question, "Fourteen days.", cacheKey: prepared.CacheKey);

        Assert.Null(prepared.Cached);
        Assert.Null(prepared.CacheKey);
        Assert.Equal(2, (await App.SearchAsync(Ana, "?mine=true")).Count);
    }

    [Fact]
    public async Task TheOwner_SeesTheInstallsSavings()
    {
        await AskedAndAnsweredAsync(Ana, "s1");
        await App.PrepareAsync(Ana, Question);

        var stats = await App.Client(Olivia).GetFromJsonAsync<AnswerCacheStats>("/api/chat/cache", ChatContextApp.Json, TestContext.Current.CancellationToken);

        Assert.Equal(0, stats!.Hits);
        Assert.Equal(1, stats.AllHits);
    }
}

/// <summary>Off by default: no Redis call is made at all.</summary>
public sealed class AnswerCacheOffTests(PostgresFixture postgres) : ChatContextTestBase(postgres)
{
    [Fact]
    public async Task Off_NeverTouchesTheStore()
    {
        var prepared = await App.PrepareAsync(Ana, "What is our refund window?");
        await App.TurnAsync(Ana, "s1", "What is our refund window?", "Fourteen days.", cacheKey: prepared.CacheKey);
        await App.PrepareAsync(Ana, "What is our refund window?");

        Assert.Null(prepared.CacheKey);
        Assert.Empty(App.Store.Values);
        Assert.Equal(0, App.Store.Reads);
        Assert.False((await App.Client(Ana).GetFromJsonAsync<AnswerCacheStats>("/api/chat/cache", ChatContextApp.Json, TestContext.Current.CancellationToken))!.Enabled);
    }
}
